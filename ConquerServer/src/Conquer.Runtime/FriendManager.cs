// Conquer.Runtime — Friend relationship manager.
// Original: MapGroupKernel/User.cpp, WorldKernel/NETWORK2/MsgFriend.cpp

using Conquer.Shared.Messages.Packets;

namespace Conquer.Runtime;

/// <summary>
/// In-memory friend list manager.
/// </summary>
public sealed class FriendManager
{
    private readonly Dictionary<uint, Dictionary<uint, string>> _friends = new();

    public bool AddFriend(uint userId, uint friendId, string friendName)
    {
        if (userId == friendId) return false;

        if (!_friends.TryGetValue(userId, out var list))
        {
            list = new Dictionary<uint, string>();
            _friends[userId] = list;
        }

        list[friendId] = friendName;
        return true;
    }

    public bool RemoveFriend(uint userId, uint friendId)
    {
        if (_friends.TryGetValue(userId, out var list))
        {
            return list.Remove(friendId);
        }
        return false;
    }

    public bool IsFriend(uint userId, uint friendId)
    {
        return _friends.TryGetValue(userId, out var list) && list.ContainsKey(friendId);
    }

    public IReadOnlyDictionary<uint, string> GetFriends(uint userId)
    {
        if (_friends.TryGetValue(userId, out var list))
        {
            return list;
        }
        return new Dictionary<uint, string>();
    }

    public static MsgFriend CreateOnlineNotification(uint friendId, string friendName, uint lookFace, byte level)
    {
        return MsgFriend.Create(
            FriendAction.Online,
            friendId,
            friendName,
            isOnline: true,
            lookFace: lookFace,
            level: level);
    }

    public static MsgFriend CreateOfflineNotification(uint friendId, string friendName)
    {
        return MsgFriend.Create(
            FriendAction.Offline,
            friendId,
            friendName,
            isOnline: false);
    }
}
