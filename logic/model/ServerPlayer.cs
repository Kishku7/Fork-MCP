using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Fork.Annotations;
using Fork.ViewModel;

namespace Fork.Logic.Model;

public class ServerPlayer : IComparable<ServerPlayer>, IEquatable<ServerPlayer>, INotifyPropertyChanged
{
    public ServerPlayer(Player player, ServerViewModel viewModel, bool isOp, bool isOnline)
    {
        Player = player;
        ServerViewModel = viewModel;
        IsOP = isOp;
        IsOnline = isOnline;
    }

    public Player Player { get; set; }
    public ServerViewModel ServerViewModel { get; set; }
    public bool IsOP { get; set; }
    public bool IsOnline { get; set; }

    /// <summary>True when the wrapped player has an offline-mode (v3) UUID. Used for UI labelling + sorting.</summary>
    public bool IsOfflineMode => Player?.IsOfflineMode ?? false;

    /// <summary>Display name with an "OM-" prefix for offline-mode players (display only).</summary>
    public string DisplayName => Player?.DisplayName;


    public int CompareTo(ServerPlayer other)
    {
        int onlineCompare = other.IsOnline.CompareTo(IsOnline);
        if (onlineCompare != 0)
        {
            return onlineCompare;
        }

        int opCompare = other.IsOP.CompareTo(IsOP);
        if (opCompare != 0)
        {
            return opCompare;
        }

        return string.Compare(Player.Name, other.Player.Name, StringComparison.Ordinal);
    }

    public bool Equals(ServerPlayer other)
    {
        if (ReferenceEquals(null, other))
        {
            return false;
        }

        if (ReferenceEquals(this, other))
        {
            return true;
        }

        return Equals(Player, other.Player) && Equals(ServerViewModel, other.ServerViewModel) && IsOP == other.IsOP &&
               IsOnline == other.IsOnline;
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public override bool Equals(object obj)
    {
        if (ReferenceEquals(null, obj))
        {
            return false;
        }

        if (ReferenceEquals(this, obj))
        {
            return true;
        }

        if (obj.GetType() != GetType())
        {
            return false;
        }

        return Equals((ServerPlayer)obj);
    }

    public override int GetHashCode()
    {
        return HashCode.Combine(Player, ServerViewModel, IsOP, IsOnline);
    }

    [NotifyPropertyChangedInvocator]
    protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}