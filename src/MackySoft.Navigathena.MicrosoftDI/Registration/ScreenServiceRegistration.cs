using System;

namespace MackySoft.Navigathena.MicrosoftDI.Registration
{
    internal enum ScreenServiceRole
    {
        Lifecycle
    }

    internal sealed class ScreenServiceRegistration
    {
        public ScreenServiceRegistration (Type type, ScreenServiceRole role)
        {
            Type = type;
            Role = role;
        }
        public Type Type
        {
            get;
        }
        public ScreenServiceRole Role
        {
            get;
        }
    }
}
