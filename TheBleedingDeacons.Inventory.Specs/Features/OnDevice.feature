@manual @ignore
Feature: On the device
  What only a real device can show. Walked through by hand on a tablet or
  handset with a cable, not run by CI.

  Scenario: A developer's build shows up in logcat under the app's own tag
    Given a Debug build of the app on an Android device
    When it starts
    Then "adb logcat -s <Application>:V" shows its log lines as they happen

  Scenario: A Java-side crash is on record after the next launch
    Given the app has been told to ship to Better Stack
    When the app dies of an unhandled Android exception
    And the app is opened again
    Then Better Stack has the crash as FATAL "Unhandled Android exception"

  Scenario: What was held before an upgrade is shipped after it
    Given a device running Register 1.3.0 or Link, holding logs
    When it is upgraded to a build on Inventory and told where to ship
    Then what it was holding reaches Better Stack

  Scenario: The device label names the device in Live Tail
    Given a device label set in the app's settings
    When the app logs anything
    Then Better Stack shows it with properties.DeviceLabel set to that label
