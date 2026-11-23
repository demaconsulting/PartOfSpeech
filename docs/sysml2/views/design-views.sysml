// Views rendered into docs/design/generated/ for the Design document.
// Imports the PartOfSpeech facade so each 'expose' statement can reference its target part def
// directly. Per SysML2Tools syntax, 'expose' is only valid
// inside a named 'view' usage (not a 'view def' definition), and it - not
// 'render' - is what scopes the rendered diagram content.
package PartOfSpeechDesignViews {
    private import StandardViewDefinitions::*;
    private import PartOfSpeech::*;

    view SoftwareStructureView : InterconnectionView {
        expose PartOfSpeech::PartOfSpeechSystem;
    }

    view PartOfSpeechView {
        expose PartOfSpeechSystem;
    }

    view TokenizationView {
        expose Tokenization;
    }

    view ModelStorageView {
        expose ModelStorage;
    }

    view TaggingView {
        expose Tagging;
    }
}
