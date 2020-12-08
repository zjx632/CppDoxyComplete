﻿using System;

namespace CppTripleSlash
{
    public enum DoxygenStyle
    {
        Qt,
        JavaDoc
    };

    public enum DoxygenFirstLineStyle
    {
        /// <summary>
        /// /**
        /// </summary>
        SlashStarStar,

        /// <summary>
        /// /*!
        /// </summary>
        SlashStarExclamation,
    }

    public class DoxygenConfig
    {
        /// <summary>
        /// Amount of indentation spaces for doxygen tags.
        /// </summary>
        public int TagIndentation { get; set; } = 0;

        /// <summary>
        /// Comment tag style.
        /// </summary>
        public DoxygenStyle TagStyle { get; set; } = DoxygenStyle.JavaDoc;

        /// <summary>
        /// Comment first line style.
        /// </summary>
        public DoxygenFirstLineStyle FirstLineStyle { get; set; } = DoxygenFirstLineStyle.SlashStarStar;

        /// <summary>
        /// Comment first line String.
        /// </summary>
        public string FirstLineString
        {
            get
            {
                switch (FirstLineStyle)
                {
                    case DoxygenFirstLineStyle.SlashStarExclamation:
                        return "/*!";

                    case DoxygenFirstLineStyle.SlashStarStar:
                    default:
                        return "/**";
                }
            }
        }

        /// <summary>
        /// File comment template.
        /// </summary>
        public string FileCommentTemplate { get; set; } = "";

        /// <summary>
        /// Tag starting character convenience getter.
        /// </summary>
        public char TagChar
        {
            get
            {
                switch (TagStyle)
                {
                    case DoxygenStyle.Qt:
                        return '\\';

                    case DoxygenStyle.JavaDoc:
                    default:
                        return '@';
                }
            }
        }

        /// <summary>
        /// If true, use single line comment
        /// </summary>
        public bool UseSingleLineComment { get; set; } = true;

        /// <summary>
        /// If true, use brief tag
        /// </summary>
        public bool UseBriefTag { get; set; } = true;

        /// <summary>
        /// If true, auto-generation tries to generate smart comments for function summary, parameters and return values.
        /// </summary>
        public bool SmartComments { get; set; } = true;

        /// <summary>
        /// If true, auto-generation creates smart comments for all kinds of functions.
        /// </summary>
        public bool SmartCommentsForAllFunctions { get; set; } = true;

        /// <summary>
        /// Abbreviations collection for unabbreviating words.
        /// </summary>
        public AbbreviationMap Abbreviations { get; set; } = new AbbreviationMap();

        /// <summary>
        /// Formatting for autogenerated comments.
        /// </summary>
        public string BriefSetterDescFormat { get; set; } = "Sets the {1}{0}.";
        public string BriefGetterDescFormat { get; set; } = "Returns the {1}{0}.";
        public string BriefBoolGetterDescFormat { get; set; } = "Returns true if the {1}{2} {0}.";
        public string ParamSetterDescFormat { get; set; } = "{0} to set.";
        public string ReturnDescFormat { get; set; } = "The {0}.";
        public string ReturnBooleanDescFormat { get; set; } = "True if {0}. False if not.";
        public string ParamBooleanFormat { get; set; } = "If true, {0}. Otherwise not {0}.";
        public string FileCommentIsHeader { get; set; } = "Declares the {0}.";
        public string FileCommentIsSource { get; set; } = "Implements the {0}.";
        public string FileCommentIsInline { get; set; } = "Implements the {0}.";

        /// <summary>
        /// Constructor.
        /// </summary>
        public DoxygenConfig()
        {
        }
    }
}
