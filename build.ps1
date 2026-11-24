# build.ps1
#
# PURPOSE:
#   Builds the solution in Release configuration and runs the unit tests, either all of them
#   (-All, the full local build and test run) or only the test projects a change affects
#   (-Changed).
#
# USAGE:
#   pwsh ./build.ps1 -Help                     Print the usage text and exit 0
#   pwsh ./build.ps1 -All                      Full local build and all tests
#   pwsh ./build.ps1 -Changed [-Base <ref>]    Only the test projects affected by the changes since <ref>
#                                              (default origin/main, else main) plus uncommitted changes
#   Add -DryRun to any scope to print the selection without building anything.
#   With no arguments the usage text is printed and the script exits 1 (a scope must be chosen).
#
#   A changed project selects every project that references it, directly or transitively, from the
#   ProjectReference graph, and files linked into another project (Compile/None/EmbeddedResource items
#   that point outside the project folder) select that project too. Changes that affect the whole
#   build (solution, project, props/targets, global.json, .editorconfig, nuget.config, tool manifest,
#   this script, scripts/ProjectScripts.psm1) fall back to the full build; repository files tests read
#   at run time select their test projects through $RuntimeReads; other changes the build does not
#   read select nothing.
#
#   PartOfSpeech has one library, one test project, two build-time tools (LexiconCompiler and the
#   model trainer), and a benchmarks project that is only built, so there is no -Project switch:
#   -Changed selects the test projects through the project reference graph.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to find the designated locations
#   for adding project-specific build or test operations.
#
# CUSTOMIZATION:
#   This script belongs to the project: extend it as the project grows. Add steps at the
#   [PROJECT-SPECIFIC] points and add optional parameters that focus a run. The reusable scoping
#   logic lives in scripts/ProjectScripts.psm1, which is delivered by the Agents release zip, is
#   shared across repositories, and must never be edited here. Slow or environment-dependent test
#   tiers (GUI automation, integration) may be opt-in switches.
#   With no arguments the script prints the usage text and exits 1; -All must still build and run
#   the standard test suite (agents rely on that), and never remove or weaken a test step to make
#   it pass.

param(
    # Full local build and all tests.
    [switch]$All,

    # Build and test only the projects affected by the changed files.
    [switch]$Changed,

    # Git reference to compare against for -Changed (default: origin/main, else main).
    [string]$Base,

    # Print the selection and exit without building.
    [switch]$DryRun,

    # Print the usage text and exit 0.
    [switch]$Help
)

Import-Module (Join-Path $PSScriptRoot 'scripts/ProjectScripts.psm1') -Force

# [PROJECT-SPECIFIC] Repository files that tests read at run time (through the repository root, not through a
# project item), mapped to the project that reads them. Keep in step with the tests. The keys are regular
# expressions, so each must be a distinct string; two keys may match the same file to select two projects.
#   - The test project reads the data, the data tools, the evaluation records, the user guide and design/verification
#     documents of the library, the package and license documents at the repository root, every file of the trainer
#     folder (training.json, README.md, and sources it inspects), and lists the whole repository for CoNLL-U files.
#   - LexiconCompiler (--check) reads data/lexicon; the trainer (train --check) reads the corpora under data/.
$RuntimeReads = [ordered]@{
    '^(data|tools/data)/'                                                                                 = 'DemaConsulting.PartOfSpeech.Tests'
    '^docs/(evaluation/|user_guide/introduction\.md$|verification/part-of-speech\.md$|design/part-of-speech(\.md$|/))' = 'DemaConsulting.PartOfSpeech.Tests'
    '^(README|PACKAGE-LICENSE|THIRD-PARTY-NOTICES)\.md$|^LICENSE$'                                        = 'DemaConsulting.PartOfSpeech.Tests'
    '^tools/DemaConsulting\.PartOfSpeech\.Trainer/'                                                       = 'DemaConsulting.PartOfSpeech.Tests'
    '\.conllu$'                                                                                           = 'DemaConsulting.PartOfSpeech.Tests'
    '^data/lexicon/'                                                                                      = 'LexiconCompiler'
    '^(data/|tools/data/)'                                                                                = 'DemaConsulting.PartOfSpeech.Trainer'
    '^\.fileassert\.yaml$'                                                                                = 'DemaConsulting.PartOfSpeech'
}

# [PROJECT-SPECIFIC] The projects the scoped build knows: the library, the test project, the benchmarks (built, never
# run), and the two build-time tools. tools/data/hmm-tags is a standalone harness outside the solution that no script
# builds, so it is left out of the scan.
$ProjectRoots = @('src', 'test', 'benchmarks', 'tools/DemaConsulting.PartOfSpeech.Trainer', 'tools/LexiconCompiler')

# ==============================================================================
# USAGE AND SCOPE VALIDATION
# ==============================================================================

function Show-Usage {
    Write-ScriptUsage -Script 'build.ps1' -IterateWith '-Changed' `
        -Purpose 'build the solution (Release), check the derived artifacts, and run the unit tests.' `
        -Options ([ordered]@{
            '-All'        = 'Full local build and all tests; cannot be combined with another scope.'
            '-Changed'    = 'Only the test projects affected by files changed since the base, plus uncommitted files.'
            '-Base <ref>' = 'Base reference for -Changed (default origin/main, else main).'
            '-DryRun'     = 'Print what would be built, tested, and skipped, then exit 0 without running anything.'
            '-Help'       = 'Print this text and exit 0.'
        }) `
        -Examples @('-Changed', '-Changed -DryRun', '-All')
}

if ($Help) { Show-Usage; exit 0 }

$scopeProblems = Test-ScopeArguments -Base $Base -Scopes ([ordered]@{
        All     = $All.IsPresent
        Changed = $Changed.IsPresent
    })
if ($scopeProblems.Count -gt 0) {
    $scopeProblems | ForEach-Object { Write-Host "Error: $_" }
    Write-Host ""
    Show-Usage
    exit 1
}

$buildError = $false

# [PROJECT-SPECIFIC] The derived-artifact checks that follow a build: the committed lexicon and the committed model
# weights must be current (each tool re-derives them and compares), and the generated API documentation must satisfy
# the FileAssert 'api-docs' tests. Sets $script:buildError when a check fails; a failed check skips the later ones.
function Invoke-DerivedArtifactChecks {
    param([bool]$Lexicon = $true, [bool]$Weights = $true, [bool]$ApiDocs = $true)

    if ($Lexicon) {
        Write-Host "Checking the compiled lexicon..."
        dotnet run --no-build --configuration Release --project tools/LexiconCompiler -- --check
        if ($LASTEXITCODE -ne 0) { $script:buildError = $true; return }
    }

    if ($Weights) {
        Write-Host "Checking the trained model..."
        dotnet run --no-build --configuration Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- train --check
        if ($LASTEXITCODE -ne 0) { $script:buildError = $true; return }
    }

    if ($ApiDocs) {
        Write-Host "Asserting the generated API documentation..."
        dotnet tool restore
        if ($LASTEXITCODE -ne 0) { $script:buildError = $true; return }
        dotnet fileassert api-docs
        if ($LASTEXITCODE -ne 0) { $script:buildError = $true; return }
    }
}

# ==============================================================================
# SCOPED BUILD (-Changed)
# Selects the affected test projects; falls back to the full build when needed.
# ==============================================================================

$scoped = $false
if (-not $All) {
    $graph = Get-ProjectGraph -Root $ProjectRoots
    $selected = [System.Collections.Generic.HashSet[string]]::new()
    $fullReason = $null

    $changedFiles = Get-ChangedFiles -Base $Base
    if ($null -eq $changedFiles) {
        $fullReason = 'the changed files could not be determined'
    } else {
        Write-Host "Changed files: $($changedFiles.Count)"
        $fullReason = Add-ChangedProjects -Graph $graph -Files $changedFiles -Selected $selected -RuntimeReads $RuntimeReads
    }

    if ($fullReason) {
        Write-Host "Full build required: $fullReason."
    } else {
        Expand-ReferencingProjects -Graph $graph -Selected $selected

        # Test projects are built and tested; other leaf projects (tools/ and benchmarks/) are built only
        $tests = @($graph.Values | Where-Object { $selected.Contains($_.Name) -and $_.Directory -like 'test/*' })
        $tools = @($graph.Values | Where-Object { $selected.Contains($_.Name) -and ($_.Directory -like 'tools/*' -or $_.Directory -like 'benchmarks/*') })
        $skipped = @($graph.Values | Where-Object { $_.Directory -like 'test/*' -and -not $selected.Contains($_.Name) })
        if ($tests.Count -eq 0 -and $tools.Count -eq 0) {
            Write-Host "Nothing to build: no changed file is read by the build."
            exit 0
        }

        # [PROJECT-SPECIFIC] The derived-artifact checks that the selection makes necessary: each tool checks its own
        # output, and the API documentation is generated by the library build.
        $checkLexicon = $selected.Contains('LexiconCompiler')
        $checkWeights = $selected.Contains('DemaConsulting.PartOfSpeech.Trainer')
        $checkApiDocs = $selected.Contains('DemaConsulting.PartOfSpeech')
        $derived = @(@(
                if ($checkLexicon) { 'lexicon' }
                if ($checkWeights) { 'weights' }
                if ($checkApiDocs) { 'api-docs' }
            ))

        Write-Host "Affected test projects (built and tested):"
        $tests | ForEach-Object { Write-Host "  $($_.Name)" }
        if ($tools.Count -gt 0) {
            Write-Host "Affected tool and benchmark projects (build only):"
            $tools | ForEach-Object { Write-Host "  $($_.Name)" }
        }
        if ($derived.Count -gt 0) { Write-Host "Derived-artifact checks: $($derived -join ', ')" }
        if ($DryRun) {
            Write-Host "Skipped test projects ($($skipped.Count)): $(($skipped | ForEach-Object { $_.Name }) -join ', ')"
            Write-Host "Dry run: nothing was built or tested."
            exit 0
        }

        $scoped = $true
        foreach ($entry in $tests + $tools) {
            Write-Host "Building $($entry.Name)..."
            dotnet build $entry.Path --configuration Release
            if ($LASTEXITCODE -ne 0) { $buildError = $true }
        }

        if (-not $buildError) {
            Invoke-DerivedArtifactChecks -Lexicon $checkLexicon -Weights $checkWeights -ApiDocs $checkApiDocs
        }

        if (-not $buildError) {
            foreach ($entry in $tests) {
                Write-Host "Running unit tests: $($entry.Name)..."
                dotnet test --project $entry.Path --configuration Release --report-trx
                if ($LASTEXITCODE -ne 0) { $buildError = $true }
            }
        } else {
            Write-Host "Skipping tests: the build or a derived-artifact check failed."
        }
    }
}

if ($DryRun -and -not $scoped) { Write-Host "Dry run: would run the full build and all tests."; exit 0 }

# ==============================================================================
# FULL BUILD (-All, or a scoped run that needs everything)
# ==============================================================================

if (-not $scoped) {
    # Each step runs only if the steps before it succeeded, so tests never run against stale binaries
    Write-Host "Building project..."
    dotnet build --configuration Release
    if ($LASTEXITCODE -ne 0) { $buildError = $true }

    # [PROJECT-SPECIFIC] Add additional build steps here.
    if (-not $buildError) { Invoke-DerivedArtifactChecks }

    if (-not $buildError) {
        Write-Host "Running unit tests..."
        dotnet test --configuration Release --report-trx
        if ($LASTEXITCODE -ne 0) { $buildError = $true }
    } else {
        Write-Host "Skipping tests: the build or a derived-artifact check failed."
    }

    # [PROJECT-SPECIFIC] Add additional test or post-build steps here.
    # Guard steps that depend on the build with: if (-not $buildError) { ... }
}

if (-not $buildError) {
    Write-Host "Build and tests completed successfully!"
}

exit ($buildError ? 1 : 0)
