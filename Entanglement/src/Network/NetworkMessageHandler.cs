using System;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using MelonLoader;

namespace Entanglement.Network
{
    [Obsolete("Please use the new method of registering methods, without a decorator! Check the example message for the new method!", true)]
    [System.AttributeUsage(System.AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public class NetworkMessageHandlerIndex : Attribute
    {
        public byte messageIndex;
        public NetworkMessageHandlerIndex(byte messageType) => messageIndex = messageType;
    }

    public abstract class NetworkMessageData { }

    public abstract class NetworkMessageHandler
    {
        public virtual byte? MessageIndex { get; } = null; 

        // FIX: Cached properties for Attributes so they aren't parsed every frame
        public bool SkipsOnLoading { get; private set; }
        public bool HandlesOnLoaded { get; private set; }

        private Type[] _attributes;
        public Type[] Attributes { 
            get => _attributes; 
            set {
                _attributes = value;
                SkipsOnLoading = _attributes.Contains(typeof(Net.SkipHandleOnLoading));
                HandlesOnLoaded = _attributes.Contains(typeof(Net.HandleOnLoaded));
            }
        }

        public void ReadMessage(NetworkMessage message, ulong sender) {
            if (SceneLoader.loading) {
                // FIX: Used cached booleans instead of allocating LINQ .Contains over reflection types
                if (SkipsOnLoading)
                    return;
                else if (HandlesOnLoaded)
                    MelonCoroutines.Start(HandleOnLoaded(message, sender));
            }
            else
                HandleMessage(message, sender);
        }

        public IEnumerator HandleOnLoaded(NetworkMessage message, ulong sender) {
            while (SceneLoader.loading)
                yield return null;

            HandleMessage(message, sender);
        }

        public abstract void HandleMessage(NetworkMessage message, ulong sender);

        public abstract NetworkMessage CreateMessage(NetworkMessageData data);
    }

    public abstract class NetworkMessageHandler<TData> : NetworkMessageHandler where TData : NetworkMessageData {
        public sealed override NetworkMessage CreateMessage(NetworkMessageData data) {
            if (data is TData tdata) {
                if (!MessageIndex.HasValue) throw new ArgumentNullException("MessageIndex is null, we can't write messages without an index!");

                NetworkMessage message = CreateMessage(tdata);
                message.messageType = MessageIndex.Value;
                return message;
            }
            else
                throw new Exception($"Provided message data was not of type {typeof(TData).Name} or was null!");
        }

        public abstract NetworkMessage CreateMessage(TData data);
    }
}