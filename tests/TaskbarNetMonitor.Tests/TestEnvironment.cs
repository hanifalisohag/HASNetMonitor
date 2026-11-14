using Xunit;

// The suite touches real Windows objects: it creates layered windows, reads the
// taskbar and decodes icons. Running those classes side by side makes them fight
// over the shell (a topmost change, for one, is applied asynchronously), which is
// what made the presence tests fail intermittently. Serialising the assembly keeps
// each test measuring its own window. The whole suite still runs in about a second.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
