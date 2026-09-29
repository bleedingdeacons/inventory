Feature: Errors ship at once
  As whoever is diagnosing a phone in someone's pocket
  I want an error on its way the moment it happens
  So that I see it tonight, not whenever the app is next opened

  Nothing is lost waiting — the buffer is on disk. What waits is arrival:
  the shipper runs on a timer, and if the OS kills the process first, the
  error waits for the next launch. Written for Link, where the symptom was
  that messages stopped appearing, so nobody opened the app to find out why.

  Background:
    Given the shipper's timer is an hour
    And the app has been told to ship to Better Stack

  Rule: An error does not wait for the timer

    Scenario: An error ships straight away, and takes what was waiting with it
      Given the app logs "Routine, and waiting"
      When the app logs an error "The message would not open"
      Then Better Stack receives "The message would not open"
      And Better Stack receives "Routine, and waiting"

    Scenario: Routine events wait for the timer
      When the app logs "Nothing to see here"
      Then Better Stack does not receive "Nothing to see here"

  Rule: A burst of errors is one flush

    Scenario: One failure, logged at every layer on the way up
      When the app logs 5 errors at once
      Then the pipeline was rebuilt once for them
