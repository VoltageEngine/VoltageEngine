---
title: DialogueMaker
sidebar_position: 2
description: Branching dialogue graphs with a node editor, typed variables, conditions, localisation tables and a Timeline track.
---

# DialogueMaker

DialogueMaker is an installable Voltage plugin (`voltage.dialoguemaker`) for branching conversations. It adds the `.vdialogue` asset, a node-graph editor window, a runtime that plays graphs in a scene, string tables for localisation, and a Timeline track for dialogue inside cutscenes. Install it in **Plugins > Plugin Manager**: **Add Plugin** accepts a local checkout, a Git URL or a release zip, and published releases appear under **Browse Plugins**.

## Authoring

Open **DialogueMaker > Dialogue Graph** to edit a `.vdialogue` graph, and **DialogueMaker > Manual** for the built-in guide. A graph is made of nodes:

| Node | Does |
|---|---|
| Line | One line spoken by a speaker. Stores a text key, not the text. |
| Choice | Options for the player; each option can be gated by a condition. |
| Condition | Branches on a variable compared with a value. |
| Set variable | Assigns a typed variable. |
| Jump | Continues at another node. |
| End | Ends the conversation with an optional tag. |

Conditions are built from dropdowns (variable, operator, value), so they can't be malformed. `DialogueGraph.Validate()` reports broken wires, choices whose options are all gated off and jump cycles while you author.

## Playing a conversation

Add `DialogueRunner` to an entity, assign its `Graph`, and connect your UI to its events:

```csharp
using Voltage.Dialogue;

var runner = GetComponent<DialogueRunner>();
runner.LineStarted += line => _ui.ShowLine(line.SpeakerId, DialogueLocalisation.TextOf(line));
runner.ChoicesPresented += (choice, options) => _ui.ShowChoices(options);
runner.Finished += tag => _ui.Hide();

runner.Play();          // or Play(fromNodeId)
runner.Advance();       // past the current line
runner.Choose(0);       // pick a visible option
runner.Cancel();
```

Playback stops only on lines and choices; condition, variable and jump nodes are walked through. `Choose(i)` indexes the visible options, so a hidden option never shifts what the player picked. Authoring mistakes end the conversation with a warning instead of throwing. `PlayOnStart` starts automatically, and `PersistVariables` keeps variables between conversations.

`DialogueSession` runs a graph with no scene at all, which suits tests and tools. Variables expose `Snapshot()` and `Restore()` for save games.

## Localisation

Lines store keys such as `ch1.greeting`. A `DialogueStringTable` is a data asset holding one locale's entries. Activate one with `DialogueLocalisation.SetTable(table)`; `DialogueLocalisation.TextOf(line)` and `Localise(key)` then return the text. A missing key resolves to the key itself and raises `KeyMissing`, so untranslated lines stay visible and easy to spot. A line can also reference a `Voice` clip.

## Cutscenes

The plugin registers a dialogue track for the [Timeline](../scripting/timelines.md). When the playhead crosses a dialogue clip, the conversation starts; it is not scrubbed or sampled, because a conversation waits for the player. Cancelling the cutscene cancels the conversation it started.

## Building from source

The plugin lives in its own repository and builds against an engine checkout:

```bash
dotnet build Voltage.DialogueMaker.csproj -c Release
dotnet msbuild Voltage.DialogueMaker.csproj -t:PackagePlugin
```

The engine is found through `-p:VoltageEnginePath=<dir>`, the `VOLTAGE_ENGINE_PATH` environment variable or a sibling `VoltageEngine` folder. [Veltius](veltius.md) can generate DialogueMaker graphs and string tables from its dialogue boards.
