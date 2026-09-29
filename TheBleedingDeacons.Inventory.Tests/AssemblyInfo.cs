// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

// Every test here that builds a pipeline swaps Serilog's global Log.Logger,
// which is one static per process. Run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
