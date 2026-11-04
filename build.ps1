# build.ps1
#
# PURPOSE:
#   Unified cross-platform build script (replaces build.bat and build.sh).
#   Builds the solution in Release configuration and runs all unit tests.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to find the designated locations
#   for adding project-specific build or test operations.
#
# MODIFICATION POLICY:
#   Only modify this file to add project-specific operations at the designated
#   [PROJECT-SPECIFIC] extension points.

$ErrorActionPreference = 'Stop'

Write-Host "Building project..."
dotnet build --configuration Release
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# [PROJECT-SPECIFIC] Add additional build steps here.

Write-Host "Checking the compiled lexicon..."
dotnet run --no-build --configuration Release --project tools/LexiconCompiler -- --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Checking the trained model..."
dotnet run --no-build --configuration Release --project tools/DemaConsulting.PartOfSpeech.Trainer -- train --check
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Asserting the generated API documentation..."
dotnet tool restore
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
dotnet fileassert api-docs
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

Write-Host "Running unit tests..."
dotnet test --configuration Release --report-trx
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }

# [PROJECT-SPECIFIC] Add additional test or post-build steps here.

Write-Host "Build and tests completed successfully!"
