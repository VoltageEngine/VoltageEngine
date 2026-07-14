namespace Voltage.Audio
{
	/// <summary>
	/// Turns on automatic "dialogue ducking": while anything plays on the Voice bus, the Music and Ambience
	/// buses dip so speech stays intelligible, then recover when the line ends. The ducking itself is driven
	/// globally by the <see cref="AudioManager"/>; this component just tunes the levels.
	/// </summary>
	// IUpdatableInPauseMode: re-push settings every frame so inspector edits apply live even when paused.
	[ComponentId("DialogueDuckingComponent")]
	public partial class DialogueDuckingComponent : Component, IUpdatable, IUpdatableInPauseMode
	{
		/// <summary>Master switch for automatic dialogue ducking.</summary>
		public bool AutoDuckEnabled = true;

		/// <summary>Level the ducked buses are held at while dialogue plays (0..1). Lower = speech more forward.</summary>
		[Range(0f, 1f)]
		public float DuckLevel = 0.35f;

		/// <summary>Seconds for the duck to engage when a line starts.</summary>
		public float AttackSeconds = 0.15f;

		/// <summary>Seconds for the duck to release after a line ends.</summary>
		public float ReleaseSeconds = 0.5f;

		public override void OnStart()
		{
			if (Enabled)
				Apply();
		}

		public virtual void Update() => Apply();

		/// <summary>Pushes these settings into the global <see cref="AudioManager"/>. Call again after editing at runtime.</summary>
		public void Apply()
		{
			if (Core.Audio == null)
				return;

			Core.Audio.AutoDuckDialogueEnabled = AutoDuckEnabled;
			Core.Audio.DialogueDuckLevel = DuckLevel;
			Core.Audio.DialogueAttackSeconds = AttackSeconds;
			Core.Audio.DialogueReleaseSeconds = ReleaseSeconds;
		}

		// Voltage.SourceGenerators emits serialization for this partial class; it holds no runtime state.
	}
}
