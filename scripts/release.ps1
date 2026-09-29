#!/usr/bin/env pwsh
<#
.SYNOPSIS
    Cuts a release of Inventory and Inventory.Maui: bumps both <Version>s, commits, tags and pushes.

.DESCRIPTION
    Pushing the tag is what triggers .github/workflows/release.yml, which re-runs
    the full CI gate and then publishes both packages to GitHub Packages and
    creates a GitHub Release. The two always share a version. Published package versions are immutable, so this script refuses to
    run unless the working tree is clean, HEAD is main and in sync with origin,
    and the tag does not already exist locally or on the remote.

.PARAMETER Version
    The version to release, without a leading "v" — e.g. 0.1.0 or 0.2.0-rc.1.

.PARAMETER AllowDirty
    Skip the clean-working-tree check. For recovering from a half-finished cut;
    not for normal use.

.EXAMPLE
    ./scripts/release.ps1 0.1.0

.EXAMPLE
    ./scripts/release.ps1 0.2.0-rc.1 -WhatIf
    Shows what would happen without touching anything.
#>
[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory, Position = 0)]
    [ValidatePattern('^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?$',
        ErrorMessage = 'Version must be <major>.<minor>.<patch> with an optional -prerelease suffix, and no leading "v".')]
    [string]$Version,

    [switch]$AllowDirty
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# Native commands don't throw on failure, so every git call goes through here.
function Invoke-Git {
    param([Parameter(ValueFromRemainingArguments)][string[]]$Arguments)

    $output = & git @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        throw "git $($Arguments -join ' ') failed:`n$output"
    }
    return $output
}

$repoRoot = Split-Path $PSScriptRoot -Parent
$projects = @(
    (Join-Path $repoRoot 'TheBleedingDeacons.Inventory/TheBleedingDeacons.Inventory.csproj'),
    (Join-Path $repoRoot 'TheBleedingDeacons.Inventory.Maui/TheBleedingDeacons.Inventory.Maui.csproj')
)
$tag = "v$Version"

foreach ($csproj in $projects) {
    if (-not (Test-Path $csproj)) {
        throw "Project not found at $csproj"
    }
}

Push-Location $repoRoot
try {
    # --- Guards. Cheap to check, expensive to get wrong: a published version
    # --- can never be reused, so a bad tag means burning a version number.

    $branch = (Invoke-Git rev-parse --abbrev-ref HEAD).Trim()
    if ($branch -ne 'main') {
        throw "Releases are cut from main; currently on '$branch'."
    }

    if (-not $AllowDirty) {
        $dirty = Invoke-Git status --porcelain
        if ($dirty) {
            throw "Working tree is not clean:`n$dirty`nCommit or stash first (or pass -AllowDirty)."
        }
    }

    Write-Host 'Fetching origin...' -ForegroundColor Cyan
    Invoke-Git fetch origin --tags --quiet | Out-Null

    $local = (Invoke-Git rev-parse HEAD).Trim()
    $remote = (Invoke-Git rev-parse origin/main).Trim()
    if ($local -ne $remote) {
        throw "main is out of sync with origin/main (local $($local.Substring(0,7)), remote $($remote.Substring(0,7))). Pull or push first."
    }

    if (Invoke-Git tag --list $tag) {
        throw "Tag $tag already exists locally. Delete it first if the release was never published."
    }
    if (Invoke-Git ls-remote --tags origin "refs/tags/$tag") {
        throw "Tag $tag already exists on origin. Pick a new version — published versions cannot be reused."
    }

    # --- Bump. Regex rather than XML round-tripping, which would reflow the
    # --- whole file and strip the comments.

    $pattern = '(?<prefix><Version>)(?<value>[^<]*)(?<suffix></Version>)'
    $needsBump = $false

    foreach ($csproj in $projects) {
        $found = [regex]::Matches((Get-Content -Raw $csproj), $pattern)
        if ($found.Count -ne 1) {
            throw "Expected exactly one <Version> element in $csproj, found $($found.Count)."
        }

        $current = $found[0].Groups['value'].Value
        if ($current -ne $Version) {
            Write-Host "$(Split-Path $csproj -Leaf): $current -> $Version" -ForegroundColor Cyan
            $needsBump = $true
        }
    }

    if (-not $needsBump) {
        # Legitimate when both were already set to this version by hand.
        Write-Host "Version already $Version; tagging without a bump commit." -ForegroundColor Yellow
    }

    if (-not $PSCmdlet.ShouldProcess("$tag at $($local.Substring(0,7))", 'Tag and push release (this publishes)')) {
        Write-Host 'Dry run — nothing changed.' -ForegroundColor Yellow
        return
    }

    if ($needsBump) {
        foreach ($csproj in $projects) {
            [regex]::Replace((Get-Content -Raw $csproj), $pattern, "`${prefix}$Version`${suffix}") |
                Set-Content -Path $csproj -NoNewline
            Invoke-Git add -- $csproj | Out-Null
        }
        Invoke-Git commit -m "release: v$Version" | Out-Null
        Write-Host "Committed release: v$Version" -ForegroundColor Green
    }

    # '-a' quoted: bare, PowerShell reads it as an abbreviation of
    # Invoke-Git's own -Arguments parameter and the call fails with "A
    # positional parameter cannot be found that accepts argument 'tag'".
    # That is how v0.1.0 ended up tagged by hand.
    Invoke-Git tag '-a' $tag '-m' "Inventory $Version" | Out-Null
    Write-Host "Tagged $tag" -ForegroundColor Green

    # Push the branch first: if the tag landed alone, the release workflow would
    # build a commit that isn't on main.
    if ($needsBump) {
        Invoke-Git push origin main | Out-Null
    }
    Invoke-Git push origin $tag | Out-Null
    Write-Host "Pushed $tag" -ForegroundColor Green

    Write-Host ''
    Write-Host "Release workflow: https://github.com/bleedingdeacons/inventory/actions/workflows/release.yml" -ForegroundColor Cyan
    Write-Host "Watch it with:    gh run watch (gh run list --workflow release.yml --limit 1 --json databaseId --jq '.[0].databaseId')" -ForegroundColor Cyan
}
finally {
    Pop-Location
}
