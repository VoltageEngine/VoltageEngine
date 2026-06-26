using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;
using Voltage.Editor.DebugUtils;

namespace Voltage.Editor.Scripting
{
	/// <summary>
	/// Renames the class declared inside a C# script file so it matches a renamed file. This keeps
	/// the convention that a script's file name matches its primary class name.
	///
	/// <para>Scope is deliberately <b>in-file and best-effort</b>: it renames the class whose name
	/// equals the old file name, plus that class's constructors and destructors. It does <b>not</b>
	/// rewrite references in other files — renaming a type is a code change, and cross-file callers
	/// (<c>new OldName()</c>, <c>typeof(OldName)</c>, <c>[RequireComponent(typeof(OldName))]</c>, …)
	/// must be updated separately. Component <b>scene</b> references are unaffected because identity
	/// lives in the frozen <c>[ComponentId]</c> alias, not the class name.</para>
	/// </summary>
	internal static class ScriptClassRenamer
	{
		/// <summary>
		/// Renames the class named <paramref name="oldName"/> (and its constructors/destructors) to
		/// <paramref name="newName"/> within the file at <paramref name="path"/>. No-op when the new
		/// name is not a valid C# identifier, when no class matches, or on any I/O error.
		/// </summary>
		public static void RenameClassInFile(string path, string oldName, string newName)
		{
			if (string.IsNullOrEmpty(oldName) || string.IsNullOrEmpty(newName) || oldName == newName)
				return;

			if (!IsValidIdentifier(newName))
			{
				EditorDebug.Log(
					$"ScriptClassRenamer: '{newName}' is not a valid C# identifier — file renamed, class left as '{oldName}'.",
					"AssetBrowser");
				return;
			}

			string code;
			try { code = File.ReadAllText(path); }
			catch (Exception ex)
			{
				Debug.Error($"[ScriptClassRenamer] Could not read '{path}': {ex.Message}");
				return;
			}

			var tree = CSharpSyntaxTree.ParseText(code, path: path);
			var root = tree.GetRoot();
			var text = tree.GetText();

			var edits = new List<TextChange>();
			bool matchedClass = false;
			foreach (var cls in root.DescendantNodes().OfType<ClassDeclarationSyntax>())
			{
				if (cls.Identifier.Text != oldName)
					continue;

				matchedClass = true;
				edits.Add(new TextChange(cls.Identifier.Span, newName));

				// Constructors/destructors are named after the class — rename their identifier token.
				foreach (var member in cls.Members)
				{
					if (member is ConstructorDeclarationSyntax ctor && ctor.Identifier.Text == oldName)
						edits.Add(new TextChange(ctor.Identifier.Span, newName));
					else if (member is DestructorDeclarationSyntax dtor && dtor.Identifier.Text == oldName)
						edits.Add(new TextChange(dtor.Identifier.Span, newName));
				}
			}

			if (!matchedClass)
				return; // no class matched the file name — leave the source untouched.

			// Rewrite in-file references to the type (e.g. typeof(Old), new Old(), a field of type Old,
			// a static singleton) so the renamed file still compiles on its own. Cross-file references
			// are not touched — that is a code change the developer must make. This is purely syntactic,
			// so an unrelated identifier with the same name in this file would also be rewritten; that is
			// rare for a type name and would surface immediately as a compile error if wrong.
			foreach (var idName in root.DescendantNodes().OfType<IdentifierNameSyntax>())
			{
				if (idName.Identifier.Text == oldName)
					edits.Add(new TextChange(idName.Identifier.Span, newName));
			}

			try
			{
				// Changes must be ordered and non-overlapping for SourceText.WithChanges.
				var newText = text.WithChanges(edits.OrderBy(c => c.Span.Start));
				File.WriteAllText(path, newText.ToString(), new UTF8Encoding(false));
			}
			catch (Exception ex)
			{
				Debug.Error($"[ScriptClassRenamer] Could not write '{path}': {ex.Message}");
			}
		}

		private static bool IsValidIdentifier(string name)
		{
			if (string.IsNullOrEmpty(name))
				return false;
			if (!(char.IsLetter(name[0]) || name[0] == '_'))
				return false;
			for (int i = 1; i < name.Length; i++)
			{
				if (!(char.IsLetterOrDigit(name[i]) || name[i] == '_'))
					return false;
			}
			// Reject reserved keywords (e.g. "class", "int").
			return SyntaxFacts.GetKeywordKind(name) == SyntaxKind.None;
		}
	}
}
