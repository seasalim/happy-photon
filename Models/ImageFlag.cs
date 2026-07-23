namespace HappyPhoton.Models;

public enum ImageFlag
{
    Unflagged = 0,
    Picked = 1,
    Rejected = 2
}

public enum FlagFilter
{
    All,
    Picked,
    Rejected
}
