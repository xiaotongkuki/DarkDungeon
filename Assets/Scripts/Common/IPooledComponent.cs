using UnityEngine;

/// <summary>
/// A component that a <see cref="ComponentPool{T}"/> can hand out and take back.
///
/// The contract is deliberately tiny. An instance only needs to know the pool that
/// owns it, so it can hand itself back when its life is over; everything it must
/// clear between uses it clears in its own <c>OnEnable</c>, which is where Unity
/// already gives it the chance and where the project's reusable components already
/// do their resetting.
///
/// The interface is parameterised by the implementer's own type rather than by
/// <see cref="Component"/>, so the pool's generic constraint is checked at compile
/// time instead of by casting at runtime. That is also why the constraint repeats the
/// interface itself: <see cref="ComponentPool{T}"/> requires its own type argument to
/// implement this interface, and without the self-reference the interface could not
/// name a pool it is compatible with.
/// </summary>
/// <typeparam name="T">The implementing component's own type.</typeparam>
public interface IPooledComponent<T> where T : Component, IPooledComponent<T>
{
    /// <summary>
    /// The pool that handed this instance out, or null when it was created directly.
    ///
    /// Assigned by the pool, so an instance never needs to know who owns it - and an
    /// instance that was never pooled reports null, which is what lets it fall back
    /// to destroying itself and keeps unpooled use working unchanged.
    /// </summary>
    ComponentPool<T> Pool { set; }
}
