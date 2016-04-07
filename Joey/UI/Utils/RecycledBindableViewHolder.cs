﻿using System;
using Android.Support.V7.Widget;
using Android.Views;

namespace Toggl.Joey.UI.Utils
{
    /// <summary>
    /// Base class for bindable view holders. Useful for list view items.
    /// </summary>
    public abstract class RecycledBindableViewHolder<T> : RecyclerView.ViewHolder
    {
        public T DataSource { get; private set; }

        protected RecycledBindableViewHolder(IntPtr a, Android.Runtime.JniHandleOwnership b) : base(a, b)
        {
        }

        protected RecycledBindableViewHolder(View root) : base(root)
        {
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DataSource = default (T);
            }

            base.Dispose(disposing);
        }

        public void Bind(T dataSource)
        {
            DataSource = dataSource;
            Rebind();
        }

        public void DisposeDataSource()
        {
            DataSource = default (T);
        }

        protected abstract void Rebind();
    }
}
