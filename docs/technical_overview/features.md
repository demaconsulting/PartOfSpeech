# Features

The tagger is a linear model over sparse features. Instead of reading a word and understanding it, the model is told a
list of simple facts about the token and its surroundings, and it has learned how much each fact points toward each
tag. This section describes the facts (the features), how they are turned into positions in a weight table by hashing,
and the switchable groups of extra features.

## What a Feature Is

A feature is a named fact that is either present or absent for a token. The name has two parts: a template that says
what kind of fact it is, and a value. For the token `valves` in the sentence `Close the bleed valves .` the template
*Suffix of 3* has the value `ves`, the template *Previous word* has the value `bleed`, and the template *Previous tag*
has the value of the tag that the decoder has just chosen for `bleed`. Each present feature carries one weight for each
of the 17 tags. The score of a tag is the sum of the weights that the present features hold for that tag (see the
Averaged Perceptron section).

There are 22 base templates, which fall into a few families:

| Family | Templates | What it captures |
| --- | --- | --- |
| Bias | one constant feature | The overall prior of each tag |
| Spelling | the folded token, and the folded lower-cased token | The identity of the word |
| Shape | one shape code | Capitalization, all capitals, digits, hyphens, apostrophes, punctuation only |
| Prefixes | the first 1, 2, 3 and 4 characters | Derivational morphology (`un-`, `re-`) |
| Suffixes | the last 1, 2, 3 and 4 characters | Inflection and derivation (`-ing`, `-ed`, `-s`, `-ly`) |
| Position | whether this is the first word or number of the sentence | Sentence-initial behavior |
| Neighbors | the words two before and two after, and the pair of the nearest two | The local context |
| Tag history | each of the two previous tags, the pair, and the previous tag with the word | What may follow what |

A prefix or a suffix exists only when the token has at least that many characters, so a token has between 15 and 22
base features. Before the first and after the last token the neighbors are fixed marker words, so the model can learn
the behavior of the first and last word of a sentence.

Each family exists for a reason. Spelling features let the model memorize the tags of frequent words. Suffix and shape
features let it guess the tag of a word that it has never seen, because most English words with the suffix `-ing` or
`-ed` behave alike, and a capitalized word in the middle of a sentence is usually a name. Neighbor features capture
the local context that separates the noun `book` in `the book` from the verb `book` in `to book`. The tag history is
what makes the tagger a sequence model: a determiner before a word makes a noun or an adjective much more likely than
a verb, and the model has learned that from the previous tag.

A linear model over such features is a deliberate choice. It is small enough to ship inside a package, it is fast, it
has no run-time dependencies, its arithmetic can be exact, and its behavior can be analyzed. A neural model could use
richer evidence, but it would be larger and slower and would not allow the integer, reproducible training described in
the Averaged Perceptron section.

## Folding

Before the features are computed, each token is folded: the text is normalized to a canonical Unicode form, typographic
quotes become the straight quote, typographic dashes become a double hyphen and the ellipsis character becomes three
periods. A lower-cased copy is made for the lower-case features. The folding applies to the features only. Folding makes
a curly apostrophe and a straight apostrophe look the same to the model, which is what the training data do, and it
never changes the text or the offsets that the caller sees. The decoder and the trainer use the same folding code, so
there is no way for them to disagree.

## Hashing

A model with a feature for every word, every suffix and every pair of neighbors has millions of distinct features.
Storing them in a dictionary from name to weight row would cost memory, time to look up the strings and a file format
with all the strings in it. The library uses the hashing trick instead: it computes a number from the template and the
value and uses that number, reduced to a fixed range, as the address of the weight row.

![Hashing: five features of the token valves are hashed to buckets that address rows of weights with one cell per tag;
two features can land in the same row and then share its weights](technical-overview-features.svg)

The figure shows the idea for one token. Each feature is mixed into a bucket number below 2^hashBits. The embedded
model uses 19 bits, a table of 524,288 buckets (the number is the `hashBits` setting of the trainer configuration). Only
buckets that hold a weight have a row, so the file stores a sparse table: for each occupied bucket, the weights of the
tags that have one. The scoring step adds up the rows of the features of the token to get 17 sums.

Hashing has clear advantages:

- **Fixed memory.** The table size is set in advance, whatever the number of distinct features.
- **No feature dictionary.** The file contains numbers only, and a feature that was never seen in training is still
  handled: it lands on some bucket like any other.
- **Speed.** Looking up a feature is a hash and a bit test, with no string comparison.

It also has a cost. Two different features can land on the same bucket, a collision, and then they share their weights.
The perceptron cannot tell them apart, so it learns one compromise weight for both. When the table is large compared
with the number of features that matter, collisions are rare, and most of them involve a rare feature whose weight is
close to zero anyway. The size of the table is a measured trade-off: on the development data, 19 bits was statistically
tied with the largest size that was tested (22 bits), while 18 bits was borderline, so the trainer configuration uses
19 bits. A second cost is that the weights cannot be read by name. For debugging, the library keeps a slow, readable
reference implementation of the features, which the tests use as the oracle for the fast one.

The consequence of hashing for compatibility is strict. A weights file is valid only for the exact features and hash
that it was trained with. The file header records the feature set and the feature groups, and the decoder follows them.

## Feature Groups

The 22 base templates look at one token, two words on each side and the two previous tags. Five switchable feature
groups add sentence-level evidence. A weights file names the groups that it was trained with, and the decoder then
computes exactly those features.

- **Position cues** add where the token sits: its position in the sentence, the first content word, the way the
  sentence ends and the length of the sentence. Where a word sits changes what it is likely to be.
- **Clause cues** add the word classes of the two neighbors, the distance to the nearest determiner or modal and a
  clause state. They separate a noun from a verb by the structure around the word.
- **Right preview** adds the tags that a first pass chose for the next two tokens. It gives the model a look at the
  right-hand context in tags and not only in words.
- **Inflection** adds whether the stem of a word that ends in `-ing`, `-ed` or `-s` is a verb or a noun in the lexicon.
  It tells `avoiding` (the stem `avoid` is a verb) from `thing` (no stem is a word).
- **Title Case line** adds whether the sentence looks like a heading, together with the capitalization of the token. A
  capital letter in a heading is not a sign of a name.

The word classes of the clause cues come from the lexicon: a token is classed as a punctuation mark, a modal, `to`, a
number, a determiner, a pronoun, a preposition, a conjunction, a word that is a noun and a verb, a noun only, a verb
only, an adjective or adverb, or other. The clause state is a small piece of memory that the decoder updates after
every decision: whether a subject-like tag and whether a verb tag have been decided since the last clause boundary such
as a comma or a conjunction. These cues help with the distinction that matters most in practice, the noun and verb
readings of the same spelling. In the imperative `Check the valve`, for example, the class of the next word (a
determiner) is a strong sign that `Check` is a verb.

The embedded model enables the clause cues only. The groups were chosen by a rule that was written before the
experiment: each of the first four groups alone, all four together and all four minus one were trained with five seeds
each and scored on a development set of hard noun and verb shapes. The rule takes the arm with the fewest groups among
those within 10% of the best result, provided that the difference from the base feature set is clearly better than
zero. The title-case group was not part of that experiment. The other groups stay in the code as switches. Turning one
on is not a setting; it needs a new training run, a new selection of the thresholds and a new held-out record.
