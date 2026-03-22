using System;
using Microsoft.Xna.Framework.Content;
using Voltage.Project;

namespace Voltage.Editor.ProjectFile
{
	public interface IGameProject
	{
		// Project info
		string ProjectName { get; }
		string ProjectPath { get; }
		ProjectSettings Settings { get; }
		Version Version { get; }

		// Asset folders
		string ScriptsFolder { get; }
		string EffectsFolder { get; }
		string ContentsFolder { get; }
		string DataFolder { get; }
		string ScenesFolder { get; }
		string PrefabsFolder { get; }

		void Initialize();
		void LoadContent(ContentManager content);
		void UnloadContent();
	}
}
