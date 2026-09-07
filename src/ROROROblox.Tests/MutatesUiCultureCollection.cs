namespace ROROROblox.Tests;

/// <summary>
/// Tests that mutate the process-wide <see cref="ROROROblox.App.Localization.TranslationSource"/>
/// singleton's culture share this collection so they never run in parallel with anything else. Since
/// the Phase D codemod (2026-09-07) the render-measure gates render <c>{loc:Loc}</c> bindings, which
/// read that same singleton — a culture mutation racing a render would measure the wrong-language
/// (wrong-height/contrast) UI. DisableParallelization isolates the mutators; each also restores the
/// culture in a finally, so nothing leaks concurrently or sequentially.
/// </summary>
[CollectionDefinition("MutatesUiCulture", DisableParallelization = true)]
public sealed class MutatesUiCultureCollection { }
