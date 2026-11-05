"""Small fixture data folder for the tests of the corrections tools (never the committed data).

`write_data_dir(root)` writes a miniature `data/` folder: one open split, MASC (with a `# genre` comment line), silver (with a masked
row), the STE challenge set and a lexicon. The sentences are chosen so that every consumer has something to find: the rules
`out-of`, `partitive-det`, `multiword-prep`, `modal-aux`, a mention candidate and a violation in the evaluation-only set.
"""
import os
import sys

sys.path.insert(0, os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
import common

OPEN_TRAIN = """# corpus = test open corpus, train split
# columns = form, upos, status
# status = unanimous | disputed | unchecked (consensus of the two annotators)

# sent_id = t1
# source_id = d1
# text = It is out of order .
It\tPRON\tunanimous
is\tVERB\tunanimous
out\tADV\tunanimous
of\tADP\tunanimous
order\tNOUN\tunanimous
.\tPUNCT\tunanimous

# sent_id = t2
# source_id = d1
# text = All of them left .
All\tPRON\tunchecked
of\tADP\tunchecked
them\tPRON\tunchecked
left\tVERB\tunchecked
.\tPUNCT\tunchecked

# sent_id = t3
# source_id = d2
# text = The word " snowball " is fun .
The\tDET\tunanimous
word\tNOUN\tunanimous
"\tPUNCT\tunanimous
snowball\tNOUN\tdisputed
"\tPUNCT\tunanimous
is\tVERB\tunanimous
fun\tADJ\tunanimous
.\tPUNCT\tunanimous
"""

OPEN_DEV = """# corpus = test open corpus, dev split
# columns = form, upos, status
# status = unanimous | disputed | unchecked (consensus of the two annotators)

# sent_id = v1
# source_id = d3
# text = Most of us came .
Most\tPRON\tunanimous
of\tADP\tunanimous
us\tPRON\tunanimous
came\tVERB\tunanimous
.\tPUNCT\tunanimous
"""

MASC = """# corpus = test MASC
# columns = form, upos, status, source_tag
# status = ptb (tag derived from the Penn Treebank bracketing)
# source_tag = the Penn Treebank tag of the token

# sent_id = masc:written/blog/x#1
# source_id = masc:written/blog/x
# text = Birds such as owls .
# genre = blog
Birds\tNOUN\tptb\tNNS
such\tADP\tptb\tIN
as\tADP\tptb\tIN
owls\tNOUN\tptb\tNNS
.\tPUNCT\tptb\t.

# sent_id = masc:written/blog/x#2
# source_id = masc:written/blog/x
# text = It is fine .
# genre = blog
It\tPRON\tptb\tPRP
is\tVERB\tptb\tVBZ
fine\tADJ\tptb\tJJ
.\tPUNCT\tptb\t.
"""

SILVER = """# corpus = test silver
# columns = form, upos, status, source_tag
# status = silver (UPOS derived by rule from the model label) | masked (no training signal; upos is _)
# source_tag = model class letter and probability in percent

# sent_id = silver-1
# source_id = gutenberg-1
# text = She can not go .
She\tPRON\tsilver\tR:100
can\tVERB\tsilver\tV:60
not\tPART\tsilver\tT:90
go\t_\tmasked\t?
.\tPUNCT\tsilver\t-
"""

STE = """# corpus = test STE challenge set
# columns = form, upos, status
# status = unanimous | disputed | unchecked

# sent_id = ste-1
# source_id = ste-doc
# text = Some of them left .
Some\tPRON\tunanimous
of\tADP\tunanimous
them\tPRON\tunanimous
left\tVERB\tunanimous
.\tPUNCT\tunanimous
"""

LEXICON = """# test lexicon
a\tDET
adp\tADP
of\tADP
out\tADP ADV
"""


def write_data_dir(root, with_dev=True):
    """Write the miniature data folder below `root` and return the path of its `data` directory."""
    data = os.path.join(root, "data")
    files = {("open", "train.tsv"): OPEN_TRAIN, ("masc", "masc.tsv"): MASC, ("silver", "silver.tsv"): SILVER,
             ("eval", "ste-challenge", "test.tsv"): STE, ("lexicon", "lexicon.tsv"): LEXICON}
    if with_dev:
        files[("open", "dev.tsv")] = OPEN_DEV
    for parts, text in files.items():
        common.write_text(os.path.join(data, *parts), text.rstrip("\n").split("\n"))
    return data
