Feature: Loggers that outlive a rebuild
  As code that was handed an ILogger when the app started
  I want my events to reach the pipeline that is running now
  So that nothing I log disappears after the first change of settings

  Every change of settings, and every flush, builds a new pipeline and
  disposes the old one. Anything that captured the old one — an ILogger<T>
  from dependency injection, above all — would go on writing into a disposed
  pipeline, silently. In Register and Link that was the Unity and Freedom
  clients, from the first rebuild on.

  Rule: An ILogger follows the pipeline

    Scenario: An ILogger made at start-up still ships after a change
      Given the app has not been told where to ship
      And an ILogger was made at start-up
      When the app is told to ship to Better Stack
      And the ILogger logs "Synced 12 members"
      Then Better Stack receives "Synced 12 members"
