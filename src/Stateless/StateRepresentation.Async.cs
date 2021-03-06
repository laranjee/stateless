#if TASKS

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Stateless
{
    public partial class StateMachine<TState, TTrigger>
    {
        internal partial class StateRepresentation
        {
            public void AddActivateAction(Func<Task> action, Reflection.InvocationInfo activateActionDescription)
            {
                _activateActions.Add(new ActivateActionBehaviour.Async(_state, action, activateActionDescription));
            }

            public void AddDeactivateAction(Func<Task> action, Reflection.InvocationInfo deactivateActionDescription)
            {
                _deactivateActions.Add(new DeactivateActionBehaviour.Async(_state, action, deactivateActionDescription));
            }

            public void AddEntryAction(TTrigger trigger, Func<Transition, object[], Task> action, Reflection.InvocationInfo entryActionDescription)
            {
                if (action == null) throw new ArgumentNullException(nameof(action));

                _entryActions.Add(
                    new EntryActionBehavior.Async((t, args) =>
                    {
                        if (t.Trigger.Equals(trigger))
                            return action(t, args);

                        return TaskResult.Done;
                    },
                    entryActionDescription));
            }

            public void AddEntryAction(Func<Transition, object[], Task> action, Reflection.InvocationInfo entryActionDescription)
            {
                _entryActions.Add(
                    new EntryActionBehavior.Async(
                        action,
                        entryActionDescription));
            }

            public void AddExitAction(Func<Transition, Task> action, Reflection.InvocationInfo exitActionDescription)
            {
                _exitActions.Add(new ExitActionBehavior.Async(action, exitActionDescription));
            }

            public async Task ActivateAsync()
            {
                if (_superstate != null)
                    await _superstate.ActivateAsync().ConfigureAwait(false);

                if (active)
                    return;

                await ExecuteActivationActionsAsync().ConfigureAwait(false);
                active = true;
            }

            public async Task DeactivateAsync()
            {
                if (!active)
                    return;

                await ExecuteDeactivationActionsAsync().ConfigureAwait(false);
                active = false;

                if (_superstate != null)
                    await _superstate.DeactivateAsync().ConfigureAwait(false);
            }

            async Task ExecuteActivationActionsAsync()
            {
                foreach (var action in _activateActions)
                    await action.ExecuteAsync().ConfigureAwait(false);
            }

            async Task ExecuteDeactivationActionsAsync()
            {
                foreach (var action in _deactivateActions)
                    await action.ExecuteAsync().ConfigureAwait(false);
            }

            public async Task EnterAsync(Transition transition, params object[] entryArgs)
            {
                if (transition.IsReentry)
                {
                    await ExecuteEntryActionsAsync(transition, entryArgs).ConfigureAwait(false);
                    await ExecuteActivationActionsAsync().ConfigureAwait(false);
                }
                else if (!Includes(transition.Source))
                {
                    if (_superstate != null)
                        await _superstate.EnterAsync(transition, entryArgs).ConfigureAwait(false);

                    await ExecuteEntryActionsAsync(transition, entryArgs).ConfigureAwait(false);
                    await ExecuteActivationActionsAsync().ConfigureAwait(false);
                }
            }

            public async Task<Transition> ExitAsync(Transition transition)
            {
                if (transition.IsReentry)
                {
                    await ExecuteDeactivationActionsAsync().ConfigureAwait(false);
                    await ExecuteExitActionsAsync(transition).ConfigureAwait(false);
                }
                else if (!Includes(transition.Destination))
                {
                    await ExecuteDeactivationActionsAsync().ConfigureAwait(false);
                    await ExecuteExitActionsAsync(transition).ConfigureAwait(false);

                    if (_superstate != null)
                    {
                        transition = new Transition(_superstate.UnderlyingState, transition.Destination, transition.Trigger);
                        return await _superstate.ExitAsync(transition).ConfigureAwait(false);
                    }
                }
                return transition;
            }

            async Task ExecuteEntryActionsAsync(Transition transition, object[] entryArgs)
            {
                foreach (var action in _entryActions)
                    await action.ExecuteAsync(transition, entryArgs).ConfigureAwait(false);
            }

            async Task ExecuteExitActionsAsync(Transition transition)
            {
                foreach (var action in _exitActions)
                    await action.ExecuteAsync(transition).ConfigureAwait(false);
            }

            async Task ExecuteInternalActionsAsync(Transition transition, object[] args)
            {
                InternalTriggerBehaviour.Async internalTransition = null;

                // Look for actions in superstate(s) recursivly until we hit the topmost superstate, or we actually find some trigger handlers.
                StateRepresentation aStateRep = this;
                while (aStateRep != null)
                {
                    if (aStateRep.TryFindLocalHandler(transition.Trigger, args, out TriggerBehaviourResult result))
                    {
                        // Trigger handler(s) found in this state
                        internalTransition = result.Handler as InternalTriggerBehaviour.Async;
                        break;
                    }
                    // Try to look for trigger handlers in superstate (if it exists)
                    aStateRep = aStateRep._superstate;
                }

                // Execute internal transition event handler
                await (internalTransition?.ExecuteAsync(transition, args)).ConfigureAwait(false);
            }

            internal Task InternalActionAsync(Transition transition, object[] args)
            {
                return ExecuteInternalActionsAsync(transition, args);
            }
        }
    }
}

#endif
