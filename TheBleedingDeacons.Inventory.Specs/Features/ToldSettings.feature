Feature: Settings the app is told, not built with
  As the intergroup
  I want the log token handed only to devices that have signed in
  So that it is in no package anyone can unzip, and can be changed at will

  The app fetches; ShippingSettings does the rest. The answer is stored, so
  the next launch ships before it has reached the network — a phone woken
  by a push, or opened with no signal, carries on as it was told.

  Rule: The next launch starts from the last answer

    Scenario: A launch with nothing stored holds
      Given nothing was stored
      When the app starts
      Then the app is holding

    Scenario: A launch after being told ships at once
      Given the device was last told to ship to Better Stack
      When the app starts
      Then the app is shipping

  Rule: Only a change rebuilds anything

    Scenario: The server gives an endpoint
      Given nothing was stored
      And the app starts
      When the server answers with a log endpoint
      Then the app is shipping
      And the answer is stored

    Scenario: The same answer on every launch
      Given the device was last told to ship to Better Stack
      And the app starts
      When the server answers with the same endpoint again
      Then the pipeline was not rebuilt again

    Scenario: No answer at all
      Given the device was last told to ship to Better Stack
      And the app starts
      When the server cannot be reached
      Then the app is shipping

  Rule: Signing out forgets

    Scenario: The member signs out
      Given the device was last told to ship to Better Stack
      And the app starts
      When the member signs out
      Then the app is holding
      And nothing is stored
