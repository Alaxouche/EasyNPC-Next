using System.Windows;

[assembly: ThemeInfo(
    ResourceDictionaryLocation.None, //where theme specific resource dictionaries are located
                                     //(used if a resource is not found in the page,
                                     // or application resource dictionaries)
    ResourceDictionaryLocation.SourceAssembly //where the generic resource dictionary is located
                                              //(used if a resource is not found in the page,
                                              // app, or any theme specific resource dictionaries)
)]

// The post-build report view model's classification rules are the part of the verifier most worth testing (a false
// "conflict" there sends users to break their load order), and testing them shouldn't require standing up a real game
// environment. Exposing internals to the test assembly keeps the test-only seam out of the public API.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Focus.Apps.EasyNpc.Tests")]
