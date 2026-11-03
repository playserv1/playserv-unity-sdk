namespace Playserv.Test.RPC2
{
    // Deliberately invalid for analyzer: circular dependency between RPC classes.
    [Rpc]
    public class CircularServiceA
    {
        public CircularServiceA(CircularServiceB serviceB)
        {
        }
    }

    [Rpc]
    public class CircularServiceB
    {
        public CircularServiceB(CircularServiceA serviceA)
        {
        }
    }
}
