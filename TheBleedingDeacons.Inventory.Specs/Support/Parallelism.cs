// Copyright (c) The Bleeding Deacons. Licensed under the MIT license.

// Scenarios run one at a time. Unlike freedom-sharp's, every one of them
// swaps Serilog's global Log.Logger, which is one static per process — two
// scenarios at once would each be logging into the other's pipeline.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
