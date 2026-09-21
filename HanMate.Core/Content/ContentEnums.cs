namespace HanMate.Core.Content;

public enum ContentKind { Word, Text, Grammar, Poem }
public enum ContentOrigin { Personal, Builtin, BuiltinSnapshot, Resource, Retained }
public enum Difficulty { Beginner, Basic, Intermediate, Advanced }
public enum SchoolStage { Primary, Junior, Senior }
public enum SourceType { TestFixture, Original, Licensed, Imported, CatalogSnapshot }
public enum BackupPolicy { Allowed, Restricted, Unverified }
public enum ReviewStatus { Draft, NeedsReview, Approved }
public enum TextUnitRole { Headword, Definition, Example, Body, GrammarExplanation, GrammarNote }
public enum TokenKind { Hanzi, Punctuation, Whitespace, Latin, Number, Symbol }
public enum AnnotationSource { Manual, PhraseDictionary, CharDictionary, Reviewed, Unknown, None }
public enum AnnotationReviewState { Confirmed, NeedsReview, Unknown, NotApplicable }
public enum SegmentKind { Speech, Layout }
public enum BoundarySource { Auto, Manual }
public enum GrammarPatternPartKind { Literal, Slot, Operator }
