using UnityEngine;
using UnityEngine.Networking;

namespace HighCommand.Orders
{
    public enum OrderKind : byte
    {
        None = 0,
        Move,        // go to a point, do not fight on the way
        AttackMove,  // go to a point, fight whatever is met
        Attack,      // kill a specific enemy
        Stop,        // drop all orders and stand
        Hold,        // stand and fight only what comes into range
        Follow,      // stay close to a friendly hero
        Retreat,     // run to a safe point, do not fight
        Patrol,      // attack-move back and forth between two points
        Interact,    // use an object (teleporter, shrine, portal ...)
        Cast,        // use one skill once, at a point or an enemy
        SeekEnemies, // go after the nearest enemy anywhere on the map, then the next one
        SeekChests   // open the chests of the map the purse can pay for, nearest first, then stop
    }

    /// <summary>Why a hero sent to an object came back without using it.</summary>
    public enum InteractFailure : byte
    {
        NoGold,       // the purse cannot pay (any more)
        CannotPay,    // another price (an item the hero lacks, lunar coins ...)
        Unreachable   // the hero did not get there
    }

    public enum Stance : byte
    {
        Aggressive = 0, // chase anything seen
        Defensive = 1,  // fight near the anchor, return afterwards
        Passive = 2     // never start a fight, only defend when hit
    }

    /// <summary>One order of a hero. Plain data; the server's HeroBrain interprets it.</summary>
    public sealed class Order
    {
        public OrderKind Kind;
        public Vector3 Point;
        public Vector3 Point2;           // patrol: the other end
        public GameObject Target;        // enemy body / friendly hero body / interactable
        public NetworkInstanceId TargetId;
        public int Slot = -1;            // Cast: 0..3 skill, 4 equipment
        public bool Focus;               // Attack as focus fire: all other targets are ignored
        public float Issued;
        public bool Errand;              // a trip to an object put in front of the hero's own orders (see HeroBrain.Errand)

        // runtime
        internal bool patrolToSecond;
        internal float startedAt;
        internal bool auto;              // the hero's own idea (a barrel nearby): a failure is not reported
        internal float walkBudget = 45f; // Interact: seconds of walking before the hero gives up (set from the walk's length)
        internal bool reachKnown;        // Interact: the spot next to the object the hero walks to
        internal Vector3 reach;

        public bool HasPoint => Kind == OrderKind.Move || Kind == OrderKind.AttackMove || Kind == OrderKind.Retreat
                                || Kind == OrderKind.Patrol || Kind == OrderKind.Hold || Kind == OrderKind.Cast;

        public override string ToString()
        {
            switch (Kind)
            {
                case OrderKind.Attack:
                case OrderKind.Follow:
                case OrderKind.Interact:
                    return $"{Kind}({(Target ? Target.name : TargetId.ToString())})";
                case OrderKind.Cast:
                    return $"Cast(slot {Slot} at {Point:0})";
                default:
                    return HasPoint ? $"{Kind}({Point:0})" : Kind.ToString();
            }
        }

        /// <summary>Where the order leads (zero for orders without a place: stop, hold, cast of equipment).</summary>
        public Vector3 Destination()
        {
            switch (Kind)
            {
                case OrderKind.Move:
                case OrderKind.AttackMove:
                case OrderKind.Retreat:
                case OrderKind.Cast:
                    return Point;
                case OrderKind.Patrol:
                    return patrolToSecond ? Point2 : Point;
                case OrderKind.Attack:
                case OrderKind.Follow:
                case OrderKind.Interact:
                    return Target ? Target.transform.position : Point;
                default:
                    return Vector3.zero;
            }
        }

        public Order Clone()
        {
            return (Order)MemberwiseClone();
        }
    }

    /// <summary>
    /// A request from a commander: "these heroes, this order". Small, serialisable, so the same object travels
    /// from the UI to the server (directly in single player, over the network in co-op).
    /// </summary>
    public sealed class OrderCommand
    {
        public int[] HeroIds = new int[0];
        public OrderKind Kind;
        public Vector3 Point;
        public Vector3 Point2;
        public NetworkInstanceId TargetId;
        public int Slot = -1;
        public bool Queue;
        public bool Focus;
        public Stance? NewStance;
        public byte Formation;      // 0 blob, 1 line, 2 column
        public bool SetAutoSlot;    // toggle auto-use of a slot instead of ordering
        public bool AutoValue;
        public bool SetRally;       // move the army's rally point to Point (no hero involved)

        public void Write(NetworkWriter w)
        {
            w.WritePackedUInt32((uint)HeroIds.Length);
            foreach (int id in HeroIds) w.WritePackedUInt32((uint)id);
            w.Write((byte)Kind);
            w.Write(Point);
            w.Write(Point2);
            w.Write(TargetId);
            w.Write((sbyte)Slot);
            w.Write(Queue);
            w.Write(Focus);
            w.Write(NewStance.HasValue);
            w.Write((byte)(NewStance ?? Stance.Aggressive));
            w.Write(Formation);
            w.Write(SetAutoSlot);
            w.Write(AutoValue);
            w.Write(SetRally);
        }

        public static OrderCommand Read(NetworkReader r)
        {
            var c = new OrderCommand();
            int n = (int)r.ReadPackedUInt32();
            if (n < 0 || n > 256) return null; // not a real selection: reject instead of allocating
            c.HeroIds = new int[n];
            for (int i = 0; i < n; i++) c.HeroIds[i] = (int)r.ReadPackedUInt32();
            c.Kind = (OrderKind)r.ReadByte();
            c.Point = r.ReadVector3();
            c.Point2 = r.ReadVector3();
            c.TargetId = r.ReadNetworkId();
            c.Slot = r.ReadSByte();
            c.Queue = r.ReadBoolean();
            c.Focus = r.ReadBoolean();
            bool hasStance = r.ReadBoolean();
            var stance = (Stance)r.ReadByte();
            c.NewStance = hasStance ? stance : (Stance?)null;
            c.Formation = r.ReadByte();
            c.SetAutoSlot = r.ReadBoolean();
            c.AutoValue = r.ReadBoolean();
            c.SetRally = r.ReadBoolean();
            return c;
        }
    }
}
