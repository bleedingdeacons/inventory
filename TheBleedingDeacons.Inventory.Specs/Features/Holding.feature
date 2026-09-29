Feature: Holding logs until told where to ship
  As an app that learns where to ship only after it has signed in
  I want what happens before then kept
  So that the failure that stopped it signing in is not the one thing missing

  An app is told where to ship by a server, after sign-in — Link by
  Fellowship, Register by Freedom — so its first minutes have nowhere to go.
  Those are exactly the minutes worth having. Until it is told, every event
  goes to a small buffer on disk; once it is told, what was held ships with
  the time it was logged, not the time it arrived.

  Rule: Not told yet means held, not lost

    Scenario: Held logs ship once the app is told
      Given the app has not been told where to ship
      And the app logs an error "Sign-in failed: the browser never came back"
      When the app is told to ship to Better Stack
      Then Better Stack receives "Sign-in failed: the browser never came back"
      And "Sign-in failed: the browser never came back" arrives stamped with the time it was logged

    Scenario: Holding sends nothing at all
      Given the app has not been told where to ship
      When the app logs "Waiting for the intergroup"
      Then Better Stack receives nothing
      And the app is holding
      And "Waiting for the intergroup" is on the device

  Rule: Told not to ship means what was held is dropped

    Scenario: The intergroup has no log endpoint
      Given the app has not been told where to ship
      And the app logs "Held, and never going anywhere"
      When the app is told not to ship
      Then the app is logging locally only
      And the buffer is gone
      And "Held, and never going anywhere" is on the device

    Scenario: A later endpoint does not resurrect what was dropped
      Given the app has not been told where to ship
      And the app logs "Dropped"
      And the app is told not to ship
      When the app is told to ship to Better Stack
      Then Better Stack does not receive "Dropped"
