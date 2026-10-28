namespace Horizon.HIDL.Lexing;

public enum TokenType : byte
{
    Let,
    Const,
    Number,
    Equals,
    Dot,
    If,
    While,
    Do,
    Break,
    Delete,
    OpenParenthesis,
    CloseParenthesis,
    BinaryOperation,
    Identifier,
    Semicolon,
    Comma,
    EndOfFile,
    Null,
    OpenBracket,
    CloseBracket,
    OpenBrace,
    CloseBrace,
    Colon,
    Function,
    Quote,
    TextLiteral,
    Exclamation,
    Equality,
    NotEquality,
    Vector,
    Comment,

    // else, for (x in list), return, continue
    Else,
    For,
    In,
    Return,
    Continue,

    // The three dots that spread an object or a list into another: { ...base, damage: 8 }
    Spread,

    // +=, -=, *=, /=, %=, with the operator as the value
    CompoundEquals,

    // The ? of a ? b : c
    Question,

    // A character the language has no use for, which only the lenient lexing of an editor hands out
    Unknown,

    // A line that starts with a #, for whoever runs the program and not for the program (see HIDLPreprocessor). The
    // parser never sees it, an editor colours it
    Directive,

    // true and false, with the word as the value
    Boolean,

    // ?. , a member of something that may be null, null if it is
    QuestionDot,

    // A text in backticks with {expressions} in it, the value is what is between the backticks as it was written
    Template,

    // => between the parameters of an arrow function and what it does, x => x * 2
    Arrow,
}
