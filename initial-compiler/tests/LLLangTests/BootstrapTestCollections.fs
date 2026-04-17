namespace LLLang.Tests

open Xunit

/// Serializes tests that mutate the shared bootstrap fixture file.
[<CollectionDefinition("Bootstrap Fixture Serial", DisableParallelization = true)>]
type BootstrapFixtureSerialCollection() =
    class
    end
