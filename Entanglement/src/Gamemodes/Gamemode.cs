using System;

namespace Entanglement.Gamemodes
{
    /// <summary>
    /// Fusion-inspired gamemode base. Host selects/starts; clients mirror state.
    /// Built-ins and modules subclass this. Combat asks <see cref="CanAttack"/>.
    /// </summary>
    public abstract class Gamemode
    {
        public abstract string Title { get; }
        public virtual string Author => "Entanglement";
        public virtual string Description => "";

        /// <summary>Barcode-style id: Author.Title</summary>
        public virtual string Id => Author + "." + Title;

        public bool IsSelected { get; internal set; }
        public bool IsReady { get; internal set; }
        public bool IsStarted { get; internal set; }

        public virtual bool ManualReady => false;
        public virtual bool AutoStopOnSceneLoad => true;

        public virtual void OnRegistered() { }
        public virtual void OnUnregistered() { }
        public virtual void OnSelected() { }
        public virtual void OnDeselected() { }
        public virtual void OnReady() { }
        public virtual void OnUnready() { }
        public virtual void OnStarted() { }
        public virtual void OnStopped() { }
        public virtual void OnLevelReady() { }
        public virtual void OnUpdate() { }

        /// <summary>Friendly-fire / team gate. Default allows all PvP.</summary>
        public virtual bool CanAttack(long attackerId, long victimId) => true;

        /// <summary>Minimum players before auto-ready (0 = no auto check).</summary>
        public virtual int MinPlayers => 0;

        public virtual bool CheckReadyConditions() {
            if (MinPlayers <= 0)
                return true;
            return Entanglement.Representation.PlayerRepresentation.RemoteCount + 1 >= MinPlayers;
        }
    }
}
