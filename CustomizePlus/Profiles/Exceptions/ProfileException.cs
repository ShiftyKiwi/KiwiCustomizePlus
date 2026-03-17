// Copyright (c) Customize+.
// Licensed under the MIT license.

using System;
using System.Runtime.Serialization;

namespace CustomizePlus.Profiles.Exceptions;

internal class ProfileException : Exception
{
    public ProfileException()
    {
    }

    public ProfileException(string? message) : base(message)
    {
    }

    public ProfileException(string? message, Exception? innerException) : base(message, innerException)
    {
    }

#pragma warning disable SYSLIB0051
    protected ProfileException(SerializationInfo info, StreamingContext context) : base(info, context)
    {
    }
#pragma warning restore SYSLIB0051
}
