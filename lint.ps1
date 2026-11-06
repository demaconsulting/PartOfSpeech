# lint.ps1
#
# PURPOSE:
#   Runs all lint checks and reports failures. Exits 1 on error.
#   Used by CI/CD as the merge gate and by the lint-fix agent
#   during pre-PR cleanup.
#
#   To auto-fix formatting issues, run fix.ps1 instead.
#
# EXTENSION POINTS:
#   Search for "[PROJECT-SPECIFIC]" comments to find the designated locations
#   for adding project-specific lint checks.
#
# MODIFICATION POLICY:
#   Only modify this file to add project-specific operations at the designated
#   [PROJECT-SPECIFIC] extension points, or to update tool versions as needed.

# ==============================================================================
# HELPER FUNCTIONS
# ==============================================================================

function Get-VenvActivateScript {
    if (Test-Path ".venv/Scripts/Activate.ps1") { return ".venv/Scripts/Activate.ps1" }  # Windows
    if (Test-Path ".venv/bin/Activate.ps1") { return ".venv/bin/Activate.ps1" }          # Linux/macOS
    return $null
}

function Initialize-PythonVenv {
    if (-not (Test-Path ".venv")) {
        python -m venv .venv
        if ($LASTEXITCODE -ne 0) { return $false }
    }

    $activateScript = Get-VenvActivateScript
    if (-not $activateScript) { return $false }
    & $activateScript
    if (-not (Get-Command deactivate -ErrorAction SilentlyContinue)) { return $false }

    $installSucceeded = $false
    try {
        pip install -r pip-requirements.txt --quiet --disable-pip-version-check
        $installSucceeded = $LASTEXITCODE -eq 0
        return $installSucceeded
    }
    finally {
        if (-not $installSucceeded -and (Get-Command deactivate -ErrorAction SilentlyContinue)) {
            deactivate 2>$null
        }
    }
}

# ==============================================================================
# LINT CHECKS
# Runs all lint checks. Exits 1 if any check fails.
# ==============================================================================

$lintError = $false

# --- PYTHON SECTION ---
# Sets up a virtual environment and runs yamllint.
Write-Host "Linting: YAML..."
$skipPython = -not (Initialize-PythonVenv)
if ($skipPython) { $lintError = $true }

if (-not $skipPython) {
    yamllint .
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
    deactivate
}

# [PROJECT-SPECIFIC] Python checks of the data tools and the corpus corrections (standard library only, no network):
# every tool compiles, the unit tests of the correction tools pass, the correction tables are valid against the corpora
# (pending or applied) and equal to what the generator writes (in the applied state it reverses the rows in a temporary
# copy first), and the conventions linter enforces the fix tier (a fix-tier violation fails the lint; the evaluation-only
# STE set is shown and never counts) and reports the check tier without failing on it. The virtual environment is left,
# so `python` is the interpreter that the workflow or the developer provides (3.10 or later).
if (-not $skipPython) {
    Write-Host "Linting: data tools and corpus corrections..."
    python -m compileall -q tools/data
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    # The runner writes the TRX evidence that ReqStream reads (the Quality Checks job uploads the artifacts folder).
    python tools/data/tests/run_trx.py --out artifacts/python-data-tools.trx --pattern "test_corrections_*.py"
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    python tools/data/corrections.py validate
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    python tools/data/make_corrections.py --check
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    python tools/data/check_conventions.py --tier fix --enforce
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    # The check tier needs judgment: report only.
    python tools/data/check_conventions.py --tier check
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
}

# --- NPM SECTION ---
# Installs npm dependencies and runs cspell and markdownlint-cli2.
Write-Host "Linting: spelling and markdown..."
$skipNpm = $false
$env:PUPPETEER_SKIP_DOWNLOAD = "true"
npm install --silent
if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipNpm = $true }

if (-not $skipNpm) {
    npx cspell --no-progress --no-color --quiet "**/*.{md,yaml,yml,json,cs,cpp,hpp,h,txt}"
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    npx markdownlint-cli2 "**/*.md"
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
}

# [PROJECT-SPECIFIC] Add additional npm-based lint checks here.
# Example (ESLint for TypeScript):
#   if (-not $skipNpm) {
#       npx eslint "src/**/*.ts"
#       if ($LASTEXITCODE -ne 0) { $lintError = $true }
#   }

# --- DOTNET LINTING SECTION ---
# Runs compliance tools: reqstream, versionmark, reviewmark, sysml2tools.
Write-Host "Linting: compliance tools..."
$skipDotnetTools = $false
dotnet tool restore > $null
if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipDotnetTools = $true }

if (-not $skipDotnetTools) {
    dotnet reqstream --lint --requirements requirements.yaml
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    dotnet versionmark --lint
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    dotnet reviewmark --lint
    if ($LASTEXITCODE -ne 0) { $lintError = $true }

    if (Test-Path docs/sysml2) {
        dotnet sysml2tools lint 'docs/sysml2/**/*.sysml'
        if ($LASTEXITCODE -ne 0) { $lintError = $true }
    }
}

# [PROJECT-SPECIFIC] Add additional dotnet tool lint checks here.
# Example:
#   if (-not $skipDotnetTools) {
#       dotnet custom-tool --lint
#       if ($LASTEXITCODE -ne 0) { $lintError = $true }
#   }

# --- DOTNET FORMATTING SECTION ---
# Verifies C# code formatting matches .editorconfig rules.
Write-Host "Linting: dotnet format..."
$skipDotnetFormat = $false
dotnet restore > $null
if ($LASTEXITCODE -ne 0) { $lintError = $true; $skipDotnetFormat = $true }

if (-not $skipDotnetFormat) {
    dotnet format --verify-no-changes --no-restore
    if ($LASTEXITCODE -ne 0) { $lintError = $true }
}

# [PROJECT-SPECIFIC] Add additional format verification checks here.
# Example (clang-format check for C/C++):
#   Get-ChildItem -Recurse -Include "*.cpp","*.hpp","*.h" | ForEach-Object {
#       $result = clang-format --dry-run --Werror $_.FullName 2>&1
#       if ($LASTEXITCODE -ne 0) { Write-Output $result; $lintError = $true }
#   }

exit ($lintError ? 1 : 0)
