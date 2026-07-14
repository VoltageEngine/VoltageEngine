# Voltage Engine — Audio System Docs

Documentation for the engine's audio system (mixer, spatial, zones, voice management, backends & DSP).

- **Feature overview (visual):** [`audio-features.html`](audio-features.html) — open in a browser. Also hosted at
  <[redacted]>.
- **Diagram sources:** [`audio-architecture.mmd`](audio-architecture.mmd), [`audio-setup.mmd`](audio-setup.mmd)
  (Mermaid — both are embedded below so they render on GitHub).

---

## 1. Architecture — internal signal flow

How audio flows from the components you place, through the `AudioManager`, to the swappable backend and the OS.

```mermaid
flowchart TD

  subgraph AUTH["AUTHORING — components you place in scenes"]
    direction LR
    SRC["AudioSourceComponent<br/>one-shots · loops · 3D<br/>fades · low-pass · reverb send"]
    ZONES["Zone components<br/>Ambience · Crowd · Music<br/>MixerSnapshot · Reverb · Ducking"]
    LIS["AudioListenerComponent<br/>(the ears)"]
  end

  subgraph MGR["Core.Audio — AudioManager (global manager)"]
    direction TB
    MIX["AudioMixer<br/>Master → Music / SFX / UI / Ambience / Voice<br/>volume · mute · solo"]
    VOICES["Voice management<br/>MaxVoices · priority stealing<br/>distance culling · virtual voices"]
    FX["Ducking · Snapshots (moods)<br/>Reverb control · per-voice fades"]
    MUSIC["MusicChannel<br/>crossfading streaming music"]
  end

  CLIP["AudioClip abstraction<br/>+ AudioDecoders  wav / ogg / mp3  (managed)"]

  subgraph BE["IAudioBackend — swappable seam"]
    direction LR
    MG["MonoGameAudioBackend<br/>default, all platforms<br/>one SoundEffectInstance per voice"]
    SW["SoftwareMixingAudioBackend<br/>opt-in · real DSP<br/>SoftwareMixer → per-voice low-pass<br/>+ shared Freeverb → 1 DynamicSoundEffectInstance"]
  end

  OUT(["OpenAL → OS audio device<br/>Windows · macOS · Linux · mobile / console"])

  SRC --> MGR
  ZONES --> MGR
  LIS -->|SetListener| VOICES
  MGR --> CLIP
  CLIP -->|"backend picks representation<br/>(SoundEffect or PCM)"| BE
  MGR --> BE
  MG --> OUT
  SW --> OUT
  SW -. "IsSupported? no → fall back" .-> MG

  classDef mgr fill:#1c222b,stroke:#f2b544,stroke-width:1.5px,color:#e8ebef;
  classDef dsp fill:#15211f,stroke:#4fd6c9,stroke-width:1.5px,color:#e8ebef;
  classDef out fill:#241d12,stroke:#f2b544,stroke-width:1.5px,color:#f2b544;
  class MIX,VOICES,FX,MUSIC mgr;
  class SW,CLIP dsp;
  class OUT out;
```

---

## 2. Setup — which entity gets which components

The practical composition guide for a game project.

```mermaid
flowchart TB

  subgraph ONCE["① SET UP ONCE PER SCENE"]
    direction LR
    PLAYER["Player / Camera entity<br/> + AudioListenerComponent <br/>exactly one — the ears"]
    SETTINGS["Audio Settings entity (persistent)<br/> + DialogueDuckingComponent <br/>global auto-duck config"]
  end

  subgraph EMIT["② EMITTERS — place at the sound's location (no collider needed)"]
    direction LR
    SND["Sound entity<br/> + AudioSourceComponent <br/>assign Clip · tick Is3D for spatial<br/>Is3D draws a range gizmo"]
    CROWD["Crowd entity<br/> + CrowdEmitterComponent <br/>assign Clips[] · Density · Radius"]
  end

  subgraph ZONES["③ TRIGGER ZONES — each needs a trigger Collider on the SAME entity"]
    direction LR
    AMB["Ambience zone<br/> + Collider (isTrigger) <br/> + AmbienceZoneComponent <br/>looping bed, fades on enter/exit"]
    MUS["Music zone<br/> + Collider (isTrigger) <br/> + MusicZoneComponent <br/>starts / crossfades a track"]
    MOOD["Mood zone<br/> + Collider (isTrigger) <br/> + MixerSnapshotZoneComponent <br/>shifts whole-mix bus levels"]
    REV["Reverb zone<br/> + Collider (isTrigger) <br/> + ReverbZoneComponent <br/>room reverb (software backend)"]
  end

  NOTE["Rules of thumb:<br/>• The Listener defines where sound is heard from — put it on whatever the player 'is'.<br/>• Emitters are just placed in the world; the Listener's distance drives 3D volume & pan.<br/>• Every Zone fires when the Player's collider enters its trigger — so the Player needs a Collider too.<br/>• Reverb & occlusion low-pass are audible only with the software backend (PreferSoftwareBackend); no-op otherwise."]

  PLAYER -. "moves through the world,<br/>entering trigger zones" .-> ZONES
  ONCE --> EMIT --> ZONES --> NOTE

  classDef ears fill:#1c222b,stroke:#f2b544,stroke-width:1.5px,color:#e8ebef;
  classDef emit fill:#15211f,stroke:#4fd6c9,stroke-width:1.4px,color:#e8ebef;
  classDef zone fill:#1c222b,stroke:#3a4453,stroke-width:1.2px,color:#e8ebef;
  classDef note fill:#241d12,stroke:#a67c2e,stroke-width:1px,color:#e8ebef;
  class PLAYER,SETTINGS ears;
  class SND,CROWD emit;
  class AMB,MUS,MOOD,REV zone;
  class NOTE note;
```

---

## Enabling the software backend (DSP)

Reverb and occlusion low-pass require the software mixing backend. It's opt-in and set **before** the
`AudioManager` is constructed (there's a commented line in `Voltage.Editor/Program.cs`):

```csharp
Voltage.Audio.AudioManager.PreferSoftwareBackend = true;
```

It probes the platform and falls back to the MonoGame backend automatically where unsupported; DSP is then a
harmless no-op. Watch the console for a `[Audio] Using SoftwareMixingAudioBackend…` line to confirm.
