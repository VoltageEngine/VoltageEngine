using System;

namespace Voltage.Audio
{
	/// <summary>
	/// A compact mono Freeverb (8 comb + 4 all-pass filters): the software mixer's single shared reverb send.
	/// Voices contribute a mono send; <see cref="Process"/> turns it into a decaying tail the mixer adds back
	/// scaled by <see cref="Wet"/>. Software backend only.
	/// </summary>
	internal sealed class Reverb
	{
		// Freeverb delay-line lengths (in samples at 44.1 kHz), scaled to the actual output rate.
		private static readonly int[] CombTuning = { 1116, 1188, 1277, 1356, 1422, 1491, 1557, 1617 };
		private static readonly int[] AllPassTuning = { 556, 441, 341, 225 };

		private const float FixedGain = 0.015f;
		private const float ScaleRoom = 0.28f;
		private const float OffsetRoom = 0.7f;
		private const float ScaleDamp = 0.4f;

		private readonly Comb[] _combs;
		private readonly AllPass[] _allpasses;

		private float _roomSize = 0.5f;
		private float _damping = 0.5f;

		/// <summary>Whether the mixer should run and mix this reverb.</summary>
		public bool Enabled;

		/// <summary>Wet level added to the output (0..1).</summary>
		public float Wet = 0.3f;

		public Reverb(int sampleRate)
		{
			float scale = sampleRate / 44100f;

			_combs = new Comb[CombTuning.Length];
			for (int i = 0; i < CombTuning.Length; i++)
				_combs[i] = new Comb(Math.Max(1, (int)(CombTuning[i] * scale)));

			_allpasses = new AllPass[AllPassTuning.Length];
			for (int i = 0; i < AllPassTuning.Length; i++)
				_allpasses[i] = new AllPass(Math.Max(1, (int)(AllPassTuning[i] * scale)));

			UpdateParams();
		}

		public void SetParams(float roomSize, float damping)
		{
			_roomSize = Clamp01(roomSize);
			_damping = Clamp01(damping);
			UpdateParams();
		}

		private void UpdateParams()
		{
			float feedback = _roomSize * ScaleRoom + OffsetRoom;
			float damp = _damping * ScaleDamp;
			foreach (var comb in _combs)
			{
				comb.Feedback = feedback;
				comb.SetDamp(damp);
			}
		}

		/// <summary>Processes one mono send sample and returns the (unscaled) wet sample.</summary>
		public float Process(float input)
		{
			float inp = input * FixedGain;
			float outv = 0f;
			for (int i = 0; i < _combs.Length; i++)
				outv += _combs[i].Process(inp);   // combs in parallel
			for (int i = 0; i < _allpasses.Length; i++)
				outv = _allpasses[i].Process(outv); // all-passes in series
			return outv;
		}

		/// <summary>Clears the reverb tail so it doesn't bleed later.</summary>
		public void Clear()
		{
			foreach (var comb in _combs) comb.Clear();
			foreach (var ap in _allpasses) ap.Clear();
		}

		private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);

		// Lowpass-feedback comb filter.
		private sealed class Comb
		{
			private readonly float[] _buffer;
			private int _index;
			private float _store;
			private float _damp1, _damp2;

			public float Feedback;

			public Comb(int size) => _buffer = new float[size];

			public void SetDamp(float damp) { _damp1 = damp; _damp2 = 1f - damp; }

			public float Process(float input)
			{
				float output = _buffer[_index];
				_store = output * _damp2 + _store * _damp1;
				_buffer[_index] = input + _store * Feedback;
				if (++_index >= _buffer.Length) _index = 0;
				return output;
			}

			public void Clear() { Array.Clear(_buffer, 0, _buffer.Length); _store = 0f; }
		}

		private sealed class AllPass
		{
			private readonly float[] _buffer;
			private int _index;
			private const float Feedback = 0.5f;

			public AllPass(int size) => _buffer = new float[size];

			public float Process(float input)
			{
				float bufout = _buffer[_index];
				float output = -input + bufout;
				_buffer[_index] = input + bufout * Feedback;
				if (++_index >= _buffer.Length) _index = 0;
				return output;
			}

			public void Clear() => Array.Clear(_buffer, 0, _buffer.Length);
		}
	}
}
