namespace CubeHaul.Domain
{
    /// <summary>cube-hunt Game.status. Won is final; Stuck becomes Playing again only through +1 Slot (B-13, B-15).</summary>
    public enum GameStatus
    {
        Playing,
        Won,
        Stuck,
    }
}
