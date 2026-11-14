---
title: AI Behaviour
sidebar_position: 1
description: State machines, behaviour trees, goal-oriented action planning and utility AI.
---

# AI Behaviour

Voltage includes four ways to structure decision making, from simplest to most flexible. All of them are plain C# and work with any component.

## Simple state machine

`SimpleStateMachine<TEnum>` (namespace `Voltage.AI.FSM`) is a component driven by an enum. Register an enter, tick and exit delegate per state (each optional) in `RegisterStates`, then set `InitialState` in `OnStart`. Registration is explicit, with no reflection, so it is safe under NativeAOT.

```csharp
using Voltage.AI.FSM;

public enum GuardState { Patrol, Chase }

public class Guard : SimpleStateMachine<GuardState>
{
    protected override void RegisterStates()
    {
        RegisterState(GuardState.Patrol, tick: PatrolTick);
        RegisterState(GuardState.Chase, enter: () => Debug.Log("Spotted!"), tick: ChaseTick);
    }

    public override void OnStart()
    {
        base.OnStart();                    // runs RegisterStates
        InitialState = GuardState.Patrol;
    }

    void PatrolTick() { if (SeesPlayer()) CurrentState = GuardState.Chase; }
    void ChaseTick()  { /* move towards the player */ }
}
```

Assigning `CurrentState` runs the old state's exit and the new state's enter; `previousState` and `elapsedTimeInState` are available to subclasses. If you override `Update`, call `base.Update()`. Ticks run in `Update`, so only in Play mode.

## State machine with state objects

`StateMachine<T>` keeps one object per state and passes a shared context (usually your component). Each `State<T>` overrides `Begin`, `Reason` (decide whether to switch), `Update(deltaTime)` and `End`.

```csharp
public class Idle : State<Enemy>
{
    public override void Reason() { if (_context.CanSeePlayer) _machine.ChangeState<Attack>(); }
    public override void Update(float dt) { }
}

_machine = new StateMachine<Enemy>(this, new Idle());
_machine.AddState(new Attack());
// in Update:
_machine.Update(Time.DeltaTime);
```

`ChangeToPreviousState()`, `PreviousState`, `ElapsedTimeInState` and the `OnStateChanged` event cover the usual needs. For hierarchical machines, nest a machine inside a state.

## Behaviour trees

`BehaviorTreeBuilder<T>` (namespace `Voltage.AI.BehaviorTrees`) builds a tree with a fluent API. Nodes return `TaskStatus.Success`, `Failure` or `Running`; action and conditional delegates may return a `TaskStatus` or a `bool` (true is success).

```csharp
using Voltage.AI.BehaviorTrees;

_tree = BehaviorTreeBuilder<Enemy>.Begin(this)
    .Selector(AbortTypes.Self)
        .ConditionalDecorator(e => e.Health < 20)
            .Action(e => e.Flee())
        .Sequence()
            .Conditional(e => e.CanSeePlayer)
            .Action(e => e.Attack())
        .EndComposite()
        .Action(e => e.Wander())
    .EndComposite()
    .Build(updatePeriod: 0.2f);

// in Update:
_tree.Tick();
```

| Kind | Nodes |
|---|---|
| Composites | `Sequence`, `Selector`, `Parallel`, `ParallelSelector`, `RandomSequence`, `RandomSelector` |
| Conditionals | `Conditional(func)`, `RandomProbability` |
| Decorators | `Inverter`, `AlwaysSucceed`, `AlwaysFail`, `Repeater(count)`, `UntilSuccess`, `UntilFail`, `ConditionalDecorator(func)` |
| Actions | `Action(func)`, `WaitAction(seconds)`, `LogAction(text)`, `SubTree(tree)` |

`AbortTypes` on a selector or sequence lets a higher-priority conditional interrupt a running branch: `Self` re-checks conditionals inside the composite, `LowerPriority` lets them abort lower-priority siblings, `Both` does both. `Tick` evaluates the tree at most once per `updatePeriod`.

## Goal-oriented action planning

GOAP chooses a sequence of actions that turns the current world state into a goal state. Describe each `Action` with preconditions, postconditions and a cost; the `ActionPlanner` searches for the cheapest plan.

```csharp
using Voltage.AI.GOAP;

var planner = new ActionPlanner();
var getAxe = new Action("get axe");
getAxe.SetPrecondition("hasAxe", false);
getAxe.SetPostcondition("hasAxe", true);
var chop = new Action("chop wood", cost: 2);
chop.SetPrecondition("hasAxe", true);
chop.SetPostcondition("hasWood", true);
planner.AddAction(getAxe);
planner.AddAction(chop);

var now = planner.CreateWorldState();  now.Set("hasAxe", false);
var goal = planner.CreateWorldState(); goal.Set("hasWood", true);
Stack<Action> plan = planner.Plan(now, goal);
```

`Action<T>` adds a typed context and an `Execute()` method. `Agent` is an abstract helper that holds the planner and actions: implement its world-state and goal methods and call `Plan()`. A planner supports up to 64 conditions.

## Utility AI

Utility AI scores every option and picks the best one, which suits agents that juggle many needs. A `Reasoner<T>` (`HighestScoreReasoner` or `FirstScoreReasoner`) holds considerations; each consideration combines appraisals (scoring functions) and runs an action when chosen.

```csharp
using Voltage.AI.UtilityAI;

var eat = new AllOrNothingConsideration<Npc>(threshold: 0.5f)
    .AddAppraisal(new ActionAppraisal<Npc>(n => n.Hunger));
eat.Action = new ActionExecutor<Npc>(n => n.Eat());

var reasoner = new HighestScoreReasoner<Npc>();
reasoner.AddConsideration(eat);
var ai = new UtilityAI<Npc>(this, reasoner, updatePeriod: 0.5f);
// in Update:
ai.Tick();
```

Considerations include `AllOrNothingConsideration`, `FixedScoreConsideration`, `SumOfChildrenConsideration` and `ThresholdConsideration`; actions include `ActionExecutor`, `CompositeAction`, `ReasonerAction` (nest a reasoner) and `ActionWithOptions`.

For finding routes, see [Pathfinding](pathfinding.md).
