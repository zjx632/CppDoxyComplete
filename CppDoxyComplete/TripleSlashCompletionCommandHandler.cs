namespace CppTripleSlash
{
    using EnvDTE;
    using Microsoft.VisualStudio;
    using Microsoft.VisualStudio.Language.Intellisense;
    using Microsoft.VisualStudio.OLE.Interop;
    using Microsoft.VisualStudio.Shell;
    using Microsoft.VisualStudio.Text;
    using Microsoft.VisualStudio.Text.Editor;
    using Microsoft.VisualStudio.TextManager.Interop;
    using System;
    using System.Runtime.InteropServices;
    using System.Text;
    using Microsoft.VisualStudio.VCCodeModel;
    using System.Text.RegularExpressions;

    public class TripleSlashCompletionCommandHandler : IOleCommandTarget
    {
        public const string CppTypeName = "C/C++";
        private IOleCommandTarget m_nextCommandHandler;
        private IWpfTextView m_textView;
        private TripleSlashCompletionHandlerProvider m_provider;
        private ICompletionSession m_session;
        private DTE m_dte;
        private Regex m_regexTagSection;
        private DoxygenConfig m_config;
        private DoxygenGenerator m_generator;

        public TripleSlashCompletionCommandHandler(
            IVsTextView textViewAdapter,
            IWpfTextView textView,
            TripleSlashCompletionHandlerProvider provider,
            DTE dte)
        {
			//AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;

			this.m_textView = textView;
            this.m_provider = provider;
            this.m_dte = dte;

            m_config = new DoxygenConfig();
            m_generator = new DoxygenGenerator(m_config);
            m_regexTagSection = new Regex(@"\*\s+\" + m_config.TagChar + @"([a-z]+)\s+(.+)$", RegexOptions.Compiled);

            // add the command to the command chain
            if (textViewAdapter != null &&
                textView != null &&
                textView.TextBuffer != null &&
                textView.TextBuffer.ContentType.TypeName == CppTypeName)
            {
                textViewAdapter.AddCommandFilter(this, out m_nextCommandHandler);
            }
        }

		public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            return m_nextCommandHandler.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }

        public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                if (VsShellUtilities.IsInAutomationFunction(m_provider.ServiceProvider))
                {
                    return m_nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);
                }

                uint commandID = nCmdID;
                char typedChar = char.MinValue;

                // Make sure the input is a char before getting it.
                if (pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR)
                {
                    typedChar = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);
                }


                if (CheckForCommentTrigger(typedChar))
                {
                    // Check the indentation level at this point.
                    GenerateComment();
                    return VSConstants.S_OK;
                }

                // Check if an auto-completion session is ongoing.
                if (IsAutoCompletionActive())
                {
                    if (TryEndCompletion(typedChar, nCmdID))
                    {
                        return VSConstants.S_OK;
                    }
                }
                else
                {
                    // Add asterisk for comments every time Enter is pressed.
                    if (pguidCmdGroup == VSConstants.VSStd2K)
                    {
                        if (nCmdID == (uint)VSConstants.VSStd2KCmdID.RETURN)
                        {
                            if (IsInsideComment())
                            {
                                NewCommentLine();
                                return VSConstants.S_OK;
                            }
                        }
                        else if (nCmdID == (uint)VSConstants.VSStd2KCmdID.TAB)
                        {
                            if (TrySmartIndent())
                            {
                                return VSConstants.S_OK;
                            }
                        }
                    }
                }

                // Pass along the command so the char is added to the buffer.
                int retVal = m_nextCommandHandler.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

                // Start auto-completion session for doxygen tags and parameter names.
                if (!IsAutoCompletionActive() && (typedChar == m_config.TagChar || typedChar == '['))
                {
                    string currentLine = m_textView.TextSnapshot.GetLineFromPosition(
                                m_textView.Caret.Position.BufferPosition.Position).GetText();
                    TextSelection ts = m_dte.ActiveDocument.Selection as TextSelection;
                    string lineToCursor = currentLine.Substring(0, ts.ActivePoint.DisplayColumn - 2);

                    if (currentLine.TrimStart().StartsWith("*"))
                    {
                        if (TriggerCompletion())
                        {
                            return VSConstants.S_OK;
                        }
                    }
                }
                else if (commandID == (uint)VSConstants.VSStd2KCmdID.BACKSPACE ||
                         commandID == (uint)VSConstants.VSStd2KCmdID.DELETE ||
                         char.IsLetter(typedChar))
                {
                    if (IsAutoCompletionActive())
                    {
                        m_session.SelectedCompletionSet.SelectBestMatch();
                        m_session.SelectedCompletionSet.Recalculate();
                        return VSConstants.S_OK;
                    }
                }

                return retVal;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
            }

            return VSConstants.E_FAIL;
        }

        /// <summary>
        /// Checks if the comment trigger was just written.
        /// </summary>
        /// <param name="typedChar">Last typed character.</param>
        /// <returns>True if the comment trigger was written. Otherwise false.</returns>
        private bool CheckForCommentTrigger(char typedChar)
        {
            // Check for only those characters which could end either of the trigger words.
            if ((typedChar == '/' || typedChar == '!' || typedChar == '*') && m_dte != null)
            {
                var currentILine = m_textView.TextSnapshot.GetLineFromPosition(m_textView.Caret.Position.BufferPosition.Position);
                int len = m_textView.Caret.Position.BufferPosition.Position - currentILine.Start.Position;
                string currentLine = m_textView.TextSnapshot.GetText(currentILine.Start.Position, len);

                // Check for /// or /*!.
                string trimmed = (currentLine + typedChar).Trim();
                return (trimmed == "///" || trimmed == "/*!" || trimmed == "/**" );
            }

            return false;
        }

        /// <summary>
        /// Check if the current line is inside a comment.
        /// </summary>
        private bool IsInsideComment()
        {
            TextSelection ts = m_dte.ActiveDocument.Selection as TextSelection;

            if (!ts.IsEmpty)
                return false;

            int lineNumber = m_textView.Caret.Position.BufferPosition.GetContainingLine().LineNumber;

            string trimmedLine = m_textView.TextSnapshot.GetLineFromLineNumber(lineNumber).GetText().TrimStart();

            // TODO: This is overly simplified and should be replaced with a solution that works also in corner cases.
            if (trimmedLine.StartsWith("*/"))
                return false;

            if (trimmedLine.StartsWith("/*!") || trimmedLine.StartsWith("/**"))
                return true;

            int i = lineNumber;
            while (trimmedLine.StartsWith("*") && i > 0 && i < m_textView.TextSnapshot.LineCount)
            {
                trimmedLine = m_textView.TextSnapshot.GetLineFromLineNumber(--i).GetText().TrimStart();

                if (trimmedLine.StartsWith("/*!") || trimmedLine.StartsWith("/**"))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Creates a new comment line based on the position of the caret and Doxygen configuration.
        /// </summary>
        /// <param name="currentLine">Current line for reference.</param>
        private void NewCommentLine()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            int lineNumber = m_textView.Caret.Position.BufferPosition.GetContainingLine().LineNumber;

            string currentLine = m_textView.TextSnapshot.GetLineFromLineNumber(lineNumber).GetText();

             

            string startSpaces = currentLine.Replace(currentLine.TrimStart(), "");
            string endSpaces = currentLine.Replace(currentLine.TrimEnd(), "");

            // Try to also guess proper indentation level based on the current line.
            int extraIndent = 0;

            int i = lineNumber;
            string loopLine = currentLine;

            while (!loopLine.StartsWith("/*!") || !loopLine.StartsWith("/**"))
            {
                if (m_regexTagSection.IsMatch(loopLine))
                {
                    extraIndent = m_config.TagIndentation;
                    break;
                }

                --i;

                if (i < 0 || i > m_textView.TextSnapshot.LineCount - 1)
                {
                    break;
                }

                loopLine = m_textView.TextSnapshot.GetLineFromLineNumber(i).GetText().TrimStart();
            }

            TextSelection ts = m_dte.ActiveDocument.Selection as TextSelection;

            ts.DeleteLeft(endSpaces.Length);

            if (currentLine.EndsWith("*/"))
            {
                ts.MoveToLineAndOffset(ts.CurrentLine, currentLine.Length - 1);
            }
            else
            {
                ts.MoveToLineAndOffset(ts.CurrentLine, currentLine.Length + 1);
            }

            

            // TODO: This adds trailing space. Get rid of it similarly to SmartIndent().
            ts.Insert(m_generator.GenerateTagStartLine(startSpaces) + new string(' ', extraIndent));
        }

        /// <summary>
        /// Returns true if the auto completion session is active.
        /// </summary>
        /// <returns>True if auto completion is active. Otherwise false.</returns>
        private bool IsAutoCompletionActive()
        {
            return m_session != null && !m_session.IsDismissed;
        }

        /// <summary>
        /// Tries to end auto completion based on the currently pressed keys.
        /// </summary>
        /// <param name="typedChar">The currently typed character, if any.</param>
        /// <param name="nCmdID">Key command id.</param>
        /// <returns>True if the auto completion committed.</returns>
        private bool TryEndCompletion(char typedChar, uint nCmdID)
        {
            // Dismiss the session on space.
            if (typedChar == ' ')
            {
                m_session.Dismiss();
            }
            // Check for a commit key.
            else if (nCmdID == (uint)VSConstants.VSStd2KCmdID.RETURN || nCmdID == (uint)VSConstants.VSStd2KCmdID.TAB)
            {
                // If the selection is fully selected, commit the current session.
                if (m_session.SelectedCompletionSet.SelectionStatus.IsSelected)
                {
                    string selectedCompletion = m_session.SelectedCompletionSet.SelectionStatus.Completion.DisplayText;
                    m_session.Commit();
                    return true;
                }
                else
                {
                    // if there is no selection, dismiss the session
                    m_session.Dismiss();
                }
            }

            return false;
        }

        /// <summary>
        /// CodeElementFromPoint
        /// </summary>
        /// <param name="fcm">FileCodeModel</param>
        /// <param name="point">VirtualPoint</param>
        /// <param name="scopes">vsCMElement</param>
        /// <returns></returns>
        private CodeElement CodeElementFromPoint(FileCodeModel fcm, VirtualPoint point, params vsCMElement[] scopes)
        {
            foreach (var scope in scopes)
            {
                CodeElement codeElement = fcm.CodeElementFromPoint(point, scope);

                if (codeElement != null)
                    return codeElement;
            }

            return null;
        }

        /// <summary>
        /// Get FileCodeModel from Document
        /// </summary>
        /// <param name="doc">Document</param>
        /// <returns>FileCodeModel</returns>
        private FileCodeModel GetFileCodeModel(Document doc)
        {
            if (doc.ProjectItem != null)
            {
                return doc.ProjectItem.FileCodeModel;
            }

            return m_dte.ItemOperations.AddExistingItem(doc.FullName).FileCodeModel;
        }

        /// <summary>
        /// Generates a Doxygen comment block to the current caret location.
        /// </summary>
        private void GenerateComment()
        {
            var currentILine = m_textView.TextSnapshot.GetLineFromPosition(m_textView.Caret.Position.BufferPosition.Position);
            int len = m_textView.Caret.Position.BufferPosition.Position - currentILine.Start.Position;
            string currentLine = m_textView.TextSnapshot.GetText(currentILine.Start.Position, len);
            string spaces = currentLine.Replace(currentLine.TrimStart(), "");
            string next2char = m_textView.TextSnapshot.GetText(currentILine.Start.Position + len, 2);

            ThreadHelper.ThrowIfNotOnUIThread();
            TextSelection ts = m_dte.ActiveDocument.Selection as TextSelection;

            // Save current care position.
            int oldLine = ts.ActivePoint.Line;
            int oldOffset = ts.ActivePoint.LineCharOffset;

            // Removing the auto inserted "*/"
            if (next2char == "*/")
            {
                ts.Delete(2);
            }

            // Check if we're at the beginning of the document and should generate a file comment.
            if (oldLine == 1)
            {
                string fileComment = m_generator.GenerateFileComment(m_dte, out int selectedLine);
                ts.DeleteLeft(2); // Removing the // part here.

                ts.Insert(fileComment);

                // Move the caret.
                ts.MoveToLineAndOffset(selectedLine + 1, 1);
                
                ts.EndOfLine();
                return;
            }

            // Search for the associated code element for which to generate the comment.
            CodeElement codeElement = null;
            ts.LineDown();
            ts.EndOfLine();

            FileCodeModel fcm = this.GetFileCodeModel(m_dte.ActiveDocument);

            if (fcm != null)
            {
                while (codeElement == null)
                {
                    codeElement = CodeElementFromPoint(fcm, ts.ActivePoint,
                        vsCMElement.vsCMElementNamespace,
                        vsCMElement.vsCMElementClass,
                        vsCMElement.vsCMElementStruct,
                        vsCMElement.vsCMElementEnum,
                        vsCMElement.vsCMElementFunction,
                        vsCMElement.vsCMElementUnion);

                    if (ts.ActivePoint.AtEndOfDocument)
                    {
                        break;
                    }

                    if (codeElement == null)
                    {
                        ts.LineDown();
                    }
                }

                // if active line is in function body, set codeElement to null
                if (codeElement is CodeFunction function && oldLine > codeElement.StartPoint.Line && oldLine < codeElement.EndPoint.Line)
                {
                    codeElement = null;
                }
            }

            // Generate the comment and add it to the document.
            string doxyComment = m_generator.GenerateComment(spaces, codeElement, "");
            ts.MoveToLineAndOffset(oldLine, oldOffset);
            ts.DeleteLeft(2); // Removing the // part here.
            ts.Insert(doxyComment);


            if (!m_generator.UseSingleLineComment(codeElement))
            {
                // Move caret to the position where the main comment will be written.
                ts.MoveToLineAndOffset(oldLine, oldOffset);
                ts.LineDown();
                ts.EndOfLine();
            }
            else
            {
                ts.MoveToLineAndOffset(oldLine, oldOffset + 2);
            }
        }


        /// <summary>
        /// Tries to do smart indentation based on the current position of the caret.
        /// </summary>
        /// <returns>If smart indentation was performed. Otherwise false.</returns>
        private bool TrySmartIndent()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Try to indent intelligently to correct location based on the previous line.
            TextSelection ts = m_dte.ActiveDocument.Selection as TextSelection;
            string prevLine = ts.ActivePoint.CreateEditPoint().GetLines(ts.ActivePoint.Line - 1, ts.ActivePoint.Line);
            bool success = m_generator.GenerateIndentation(ts.ActivePoint.LineCharOffset, prevLine, out int newOffset);

            if (success)
            {
                // If we're at the end of the line, we should just move the caret. This ensures that the editor doesn't
                // commit any trailing spaces unless user writes something after the indentation.
                if (ts.ActivePoint.LineCharOffset > ts.ActivePoint.LineLength)
                {
                    ts.MoveToLineAndOffset(ts.ActivePoint.Line, newOffset);
                }
                else
                {
                    // Otherwise add indentation in the middle of the line.
                    ts.Insert(new string(' ', newOffset - ts.ActivePoint.LineCharOffset));
                }
            }

            return success;
        }

        private bool TriggerCompletion()
        {
            try
            {
                if (m_session != null)
                {
                    return false;
                }

                // the caret must be in a non-projection location 
                SnapshotPoint? caretPoint =
                m_textView.Caret.Position.Point.GetPoint(
                    textBuffer => (!textBuffer.ContentType.IsOfType("projection")), PositionAffinity.Predecessor);
                if (!caretPoint.HasValue)
                {
                    return false;
                }

                m_session = m_provider.CompletionBroker.CreateCompletionSession(
                    m_textView,
                    caretPoint.Value.Snapshot.CreateTrackingPoint(caretPoint.Value.Position, PointTrackingMode.Positive),
                    true);

                // subscribe to the Dismissed event on the session 
                m_session.Dismissed += this.OnSessionDismissed;
                m_session.Start();
                return true;
            }
            catch
            {
            }

            return false;
        }

        private void OnSessionDismissed(object sender, EventArgs e)
        {
            if (m_session != null)
            {
                m_session.Dismissed -= this.OnSessionDismissed;
                m_session = null;
            }
        }
    }
}