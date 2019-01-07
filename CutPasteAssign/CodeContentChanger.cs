using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Tagging;
using Microsoft.VisualStudio.Utilities;

/// <summary>
/// A class to be a Visual Studio extension which automatically pastes variable names where states were removed from.
/// </summary>
namespace CutPasteAssign
{
	[Export(typeof(ITaggerProvider))]
	[TagType(typeof(ITextMarkerTag))]
	[ContentType("code")]
	internal sealed class CodeTaggerProvider : ITaggerProvider
	{
		public ITagger<T> CreateTagger<T>(ITextBuffer buffer) where T : ITag
		{
			Func<ITagger<T>> sc = delegate()
			{
				return new TextBufferTracker(buffer) as ITagger<T>;
			};

			return buffer.Properties.GetOrCreateSingletonProperty<ITagger<T>>(sc);
		}
	}

	internal class TextBufferTracker : ITagger<ITextMarkerTag>
	{
		private ITextChange lastCutChange = null;
		private ITextBuffer buffer;
		private bool opportunityToWrite = false;

		public TextBufferTracker(ITextBuffer buffer)
		{
			this.buffer = buffer;

			buffer.Changed += TextBuffer_Changed;
		}

		void TextBuffer_Changed(object sender, TextContentChangedEventArgs e)
		{
			opportunityToWrite = false;
			foreach (var change in e.Changes)
			{
				if (null != lastCutChange && String.Equals(lastCutChange.OldText, change.NewText))
				{
					ITextSnapshotLine line = buffer.CurrentSnapshot.GetLineFromPosition(change.NewPosition);
					String textLine = line.GetText();
					int startOfChange = Math.Max(textLine.IndexOf(lastCutChange.OldText), 0);
					int currentPosition = textLine.LastIndexOf('=', startOfChange) - 1;
					String variableName = null;
					int variableStartPosition = -1;
					int variableEndPosition = -1;

					while (0 < currentPosition && -1 == variableStartPosition)
					{
						if (Char.IsWhiteSpace(textLine[currentPosition]))
						{
							if (0 < variableEndPosition)
							{
								variableStartPosition = currentPosition + 1;
								variableName = textLine.Substring(variableStartPosition, variableEndPosition - variableStartPosition + 1);
								opportunityToWrite = true;
							}
						}
						else
						{
							if (-1 == variableEndPosition)
							{
								variableEndPosition = currentPosition;
							}
						}
						currentPosition--;
					}

					if (!String.IsNullOrEmpty(variableName))
					{
						int insertPosition = lastCutChange.OldPosition + change.Delta;
						var ui = TaskScheduler.FromCurrentSynchronizationContext();
						Task.Factory.StartNew(() =>
						{
							try
							{
								while (opportunityToWrite && buffer.EditInProgress)
								{
									Thread.SpinWait(10);
								}
								if (opportunityToWrite)
								{
									buffer.Insert(insertPosition, variableName);
								}
							}
							catch (Exception ex)
							{
								System.Diagnostics.Debug.WriteLine(ex);
							}
						}, CancellationToken.None, TaskCreationOptions.None, ui);
						lastCutChange = null;
					}
				}
				else
				{
					lastCutChange = null;
				}
				if (-1 > change.Delta && -255 < change.Delta && change.NewSpan.IsEmpty && 0 == change.LineCountDelta)
				{
					// At this point the change hasn't made it to the Clipboard yet, so it's not reliable to check here
					lastCutChange = change;
				}
			}
		}

		#region ITagger<ITextMarkerTag> Members

		public IEnumerable<ITagSpan<ITextMarkerTag>> GetTags(NormalizedSnapshotSpanCollection spans)
		{
			yield break;
		}

		public event EventHandler<SnapshotSpanEventArgs> TagsChanged;

		#endregion
	}
}
