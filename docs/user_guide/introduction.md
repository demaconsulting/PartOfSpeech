# Introduction

## Purpose

This document is the user guide for the Part Of Speech, a demonstration project that
showcases best practices for DEMA Consulting DotNet Libraries.

## Scope

This user guide covers:

- Installation of the library
- Basic usage and examples
- API reference
- Data and license credits

# Continuous Compliance

This template follows the
[Continuous Compliance](https://github.com/demaconsulting/ContinuousCompliance) methodology, which ensures
compliance evidence is generated automatically on every CI run.

## Key Practices

- **Requirements Traceability**: Every requirement is linked to passing tests, and a trace matrix is
  auto-generated on each release
- **Linting Enforcement**: markdownlint, cspell, and yamllint are enforced before any build proceeds
- **Automated Audit Documentation**: Each release ships with generated requirements, justifications,
  trace matrix, and quality reports
- **CodeQL and SonarCloud**: Security and quality analysis runs on every build

# Installation

Install the library using the .NET CLI:

```bash
dotnet add package PartOfSpeech
```

# Usage

## Basic Usage

```csharp
using PartOfSpeech;

var demo = new Demo();
var result = demo.DemoMethod("World");
Console.WriteLine(result); // Output: Hello, World!
```

## API Reference

### Demo

The `Demo` class provides demonstration functionality for the template library.

#### Constants

##### DefaultPrefix

```csharp
public const string DefaultPrefix = "Hello";
```

The greeting prefix used when no custom prefix is specified.

#### Constructors

##### Demo()

```csharp
public Demo()
```

Initializes a new instance of the `Demo` class with the default prefix "Hello".

##### Demo(string prefix)

```csharp
public Demo(string prefix)
```

Initializes a new instance of the `Demo` class with a custom prefix.

**Parameters:**

- `prefix` (string): The prefix to use in greetings. Must not be null or empty.

**Exceptions:**

- `ArgumentNullException`: Thrown when `prefix` is null.
- `ArgumentException`: Thrown when `prefix` is an empty string.

#### Properties

##### Prefix

```csharp
public string Prefix { get; }
```

Gets the greeting prefix used by this instance.

#### Methods

##### DemoMethod

```csharp
public string DemoMethod(string name)
```

Returns a greeting message for the specified name.

**Parameters:**

- `name` (string): The name to greet. Must not be null or empty.

**Returns:**

A string containing the greeting message in the format "{prefix}, {name}!".

**Exceptions:**

- `ArgumentNullException`: Thrown when `name` is null.
- `ArgumentException`: Thrown when `name` is an empty string.

**Example:**

```csharp
var demo = new Demo();
var greeting = demo.DemoMethod("World");
// greeting = "Hello, World!"
```

# Examples

## Example 1: Basic Greeting

```csharp
using PartOfSpeech;

var demo = new Demo();
var result = demo.DemoMethod("Alice");
Console.WriteLine(result);
// Output: Hello, Alice!
```

## Example 2: Custom Prefix

```csharp
using PartOfSpeech;

var demo = new Demo("Hi");
var result = demo.DemoMethod("Alice");
Console.WriteLine(result);
// Output: Hi, Alice!
```

## Example 3: Error Handling

```csharp
using PartOfSpeech;

var demo = new Demo();
try
{
    var result = demo.DemoMethod(null);
}
catch (ArgumentNullException ex)
{
    Console.WriteLine("Name cannot be null");
}
```

# Data and Licenses

The library is MIT licensed. The repository also holds open data for the 17 Universal POS tags: training corpora, a
part-of-speech lexicon and evaluation sets, described in the
[data folder](https://github.com/demaconsulting/PartOfSpeech/blob/main/data/README.md). The library does not use the data
at run time yet. The attribution and license notices of the third-party data are in
[THIRD-PARTY-NOTICES.md](https://github.com/demaconsulting/PartOfSpeech/blob/main/THIRD-PARTY-NOTICES.md), which is also
packed at the root of the NuGet package.

## Credits

- **MASC**, the Manually Annotated Sub-Corpus of the Open American National Corpus, is licensed CC BY 3.0 US
  (<https://creativecommons.org/licenses/by/3.0/us/>). Its Penn Treebank parts of speech were converted to Universal POS
  tags.
- **Open English WordNet** is licensed CC BY 4.0 (<https://creativecommons.org/licenses/by/4.0/>) and carries the
  Princeton WordNet notice. Its word forms were reduced to a lexicon of spellings and the Universal POS tags they may
  carry.
- **The open part-of-speech corpus** of the WordSense project (<https://github.com/demaconsulting/WordSense>): the
  annotations and the selection are CC0 1.0, and the sentences are public domain in the United States or come from the
  Tatoeba CC0 export. The silver data (Tatoeba and Project Gutenberg text) and the evaluation sets follow the same terms.

## Evaluation-Only Data

The UD English-EWT treebank (CC BY-SA 4.0) is used for evaluation only. It is not distributed, nothing is trained on it,
and the repository ignores its files. No CC BY-SA or NonCommercial data is committed to the repository or included in the
package.

# References

- [REF-1] Continuous Compliance Methodology (<https://github.com/demaconsulting/ContinuousCompliance>)
