Feature: Shipping to Better Stack
  As whoever reads the logs from a fleet of phones and tablets
  I want every event to arrive, in order, with its own time
  So that a device that was offline for an evening still tells the truth

  Disk first, always. An event is in the buffer file the moment it is
  written, and ships from there on a timer. A device with no signal just
  buffers; one killed by the OS ships its backlog on the next launch.

  Rule: What arrives is in the shape Better Stack reads

    Scenario: An event arrives as Better Stack expects it
      Given the app has been told to ship to Better Stack
      When the app logs "Message 4242 delivered"
      Then Better Stack receives "Message 4242 delivered"
      And it arrives as INFO under the source token
      And "Message 4242 delivered" arrives stamped with the time it was logged

    Scenario: A bare hostname is enough
      # Better Stack's dashboard shows the endpoint without a scheme, and
      # that is what gets pasted. Register shipped nothing for months over it.
      Given the app has been told to ship to Better Stack
      Then the app is shipping

  Rule: A refused batch is kept, not lost

    Scenario: Better Stack is having a bad moment
      Given the app has been told to ship to Better Stack
      And Better Stack refuses the next batch
      When the app logs "Worth the wait"
      Then Better Stack receives "Worth the wait"

  Rule: Changing where to ship replaces the sink, never adds one

    Scenario: The token is rotated
      Given the app has been told to ship to Better Stack
      When the token changes to "the-new-token"
      And the app logs "After the rotation"
      Then Better Stack receives "After the rotation"
      And it arrives under the token "the-new-token"
      And "After the rotation" is on the device once

    Scenario: New settings that cannot be built leave logging running
      Given the app has been told to ship to Better Stack
      And new settings cannot be built
      When the token changes to "the-new-token"
      Then a warning on the device says the new settings could not be used
      And the app is shipping
      And logging carries on
