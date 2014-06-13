﻿using System;

namespace Toggl.Phoebe.Data.NewModels
{
    /// <summary>
    /// Foreign relation attribute to keep track of foreign key relations in models.
    /// </summary>
    [AttributeUsage (AttributeTargets.Property, Inherited = false, AllowMultiple = false)]
    public sealed class ForeignRelationAttribute : Attribute
    {
        public ForeignRelationAttribute ()
        {
            Required = true;
        }

        public bool Required { get; set; }
    }
}
