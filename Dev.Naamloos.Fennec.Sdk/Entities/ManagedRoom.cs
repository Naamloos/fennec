using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text;
using uniffi.matrix_sdk_ffi;

namespace Dev.Naamloos.Fennec.Sdk.Entities
{
    public partial class ManagedRoom : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public string? DisplayName
        {
            get => _displayName;
            set
            {
                _displayName = value;
                OnPropertyChanged();
            }
        }
        private string? _displayName;

        public string? Id
        {
            get => _id;
            set
            {
                _id = value;
                OnPropertyChanged();
            }
        }
        private string? _id;

        public string? AvatarUrl
        {
            get => _avatarUrl;
            set
            {
                _avatarUrl = value;
                OnPropertyChanged();
            }
        }
        private string? _avatarUrl;

        public bool IsSpace
        {
            get => _isSpace;
            set
            {
                _isSpace = value;
                OnPropertyChanged();
            }
        }
        private bool _isSpace;

        public Room NativeRoom
        {
            get => _room;
            set
            {
                _room = value;
                OnPropertyChanged();
            }
        }
        private Room _room;

        public ManagedRoom(Room room)
        {
            this.Update(room);
            if(_room is null)
            {
                throw new ArgumentNullException(nameof(room), "Room cannot be null.");
            }
        }

        public void Update(Room room)
        {
            this._room = room;
            this.DisplayName = room.DisplayName();
            this.Id = room.Id();
            this.AvatarUrl = room.AvatarUrl();
            this.IsSpace = room.IsSpace();
        }

        private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            this.PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
