using Microsoft.Xna.Framework.Content;
using System;
using System.Collections.Generic;

namespace Voltage.Editor.ProjectManagement
{
	public interface IGameProject
	{
		// Project info
		string ProjectName { get; }
		string ProjectPath { get; }
		GameSettings Settings { get; }
		Version Version { get; }

		// Asset folders
		string ScriptsFolder { get; }
		string EffectsFolder { get; }
		string ContentsFolder { get; }

		void Initialize();
		Scene CreateInitialScene();
		void LoadContent(ContentManager content);
		void UnloadContent();
	}
}
