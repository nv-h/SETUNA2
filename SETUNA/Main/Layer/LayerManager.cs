﻿using System;
using System.Collections.Generic;
using System.Windows.Forms;
using SETUNA.Main.StyleItems;

namespace SETUNA.Main.Layer
{
    public class LayerManager
    {
        public static readonly LayerManager Instance = new LayerManager();

        // Cache all open forms
        private Dictionary<IntPtr, FormData> formDic;

        // Hierarchy related
        private List<FormData> sortingFormDatas;
        private int maxSortingOrder;

        // Sticky form
        private FormData topMostFormData;

        // Level refresh Suspend switch
        private int isSuspendCount = 0;

        // Form filter
        private IWindowFilter windowFilter;


        public void Init()
        {
            formDic = new Dictionary<IntPtr, FormData>(50);
            sortingFormDatas = new List<FormData>(50);
            windowFilter = new WindowsFilter();

            WindowManager.WindowActived += WindowManager_WindowActived;
            WindowManager.TopMostChanged += WindowManager_TopMostChanged;
            FormManager.Showed += FormManager_Showed;
            FormManager.Closed += FormManager_Closed;
        }

        public void DelayInit()
        {
            FormManager.Activated += FormManager_Activated;
            FormManager.Deactivated += FormManager_Deactivated;
        }

        public void DeInit()
        {
            WindowManager.WindowActived -= WindowManager_WindowActived;
            WindowManager.TopMostChanged -= WindowManager_TopMostChanged;
            FormManager.Activated -= FormManager_Activated;
            FormManager.Deactivated -= FormManager_Deactivated;

            windowFilter = null;
            sortingFormDatas = null;
            formDic = null;
        }


        public void SuspendRefresh()
        {
            isSuspendCount++;
        }

        public void ResumeRefresh()
        {
            isSuspendCount = Math.Max(0, isSuspendCount - 1);
        }

        public int GetNextSortingOrder()
        {
            if (maxSortingOrder > 1000)
            {
                OptimizeLayerCounter();
            }

            return ++maxSortingOrder;
        }

        public void RefreshLayer()
        {
            Console.WriteLine("RefreshLayer");

            sortingFormDatas.Clear();
            sortingFormDatas.AddRange(formDic.Values);
            sortingFormDatas.Sort((x, y) => x.SortingOrder.CompareTo(y.SortingOrder));

            foreach (var item in sortingFormDatas)
            {
                if (!item.Visible)
                {
                    continue;
                }

                item.TopMost = true;
            }
        }

        public void OptimizeLayerCounter()
        {
            var forms = new List<FormData>(formDic.Values);
            forms.Sort((x, y) => x.SortingOrder.CompareTo(y.SortingOrder));

            var noDuplicates = new List<FormData>(forms);
            for (var i = 0; i < noDuplicates.Count; i++)
            {
                var item = noDuplicates[i];
                var removeCount = noDuplicates.RemoveAll(x => x.SortingOrder == item.SortingOrder && x != item);
            }
            maxSortingOrder = noDuplicates.Count - 1;

            var sortingValueDic = new Dictionary<int, int>(noDuplicates.Count);
            for (var i = 0; i < noDuplicates.Count; i++)
            {
                sortingValueDic.Add(noDuplicates[i].SortingOrder, i);
            }

            foreach (var item in forms)
            {
                item.SortingOrder = sortingValueDic[item.SortingOrder];
            }
        }

        void WindowManager_WindowActived(object sender, WindowInfo windowInfo)
        {
            CheckRefreshLayer(windowInfo);
        }

        void WindowManager_TopMostChanged(object sender, WindowInfo windowInfo)
        {
            CheckRefreshLayer(windowInfo);
        }

        void CheckRefreshLayer(WindowInfo windowInfo)
        {
            // Whether to hang
            if (isSuspendCount > 0)
            {
                return;
            }

            // Whether the current project's form
            if (formDic.ContainsKey(windowInfo.Handle))
            {
                return;
            }

            // Whether to filter
            if ((windowFilter?.IsFilter(windowInfo) ?? false == true))
            {
                return;
            }

#if DEBUG
            Console.WriteLine($"Opened:{windowInfo}");
#endif

            var topMostInfo = topMostFormData?.WindowInfo ?? WindowInfo.Empty;
            if (topMostInfo != WindowInfo.Empty)
            {
                // Comparison of the top-level form of the current project with other Windows programs Sort value
                if (topMostInfo.ZOrder >= windowInfo.ZOrder)
                {
                    return;
                }

                var hasIntersect = false;
                foreach (var item in formDic.Values)
                {
                    var childInfo = item.WindowInfo;

                    // Compare all open windows of the current project with other Windows programs.
                    if (childInfo.Rect.IntersectsWith(windowInfo.Rect))
                    {
                        hasIntersect = true;
                        break;
                    }
                }

                if (!hasIntersect)
                {
                    return;
                }

#if DEBUG
                Console.WriteLine($"TopMost: {topMostInfo}");
#endif
            }

            RefreshLayer();

        }

        void FormManager_Showed(Form form)
        {
            //Console.WriteLine("Showed:" + form); ;

            if (form != null)
            {
                var sortingOrder = 0;
                ScrapBase scrapBase = null;

                if (form is ScrapBase scrap)
                {
                    scrapBase = scrap;
                }
                else if (form is CompactScrap compact)
                {
                    scrapBase = compact.scrap;
                }

                if (scrapBase != null)
                {
                    sortingOrder = scrapBase.CacheItem.SortingOrder;
                    maxSortingOrder = Math.Max(sortingOrder, maxSortingOrder);
                    scrapBase.CacheItem.SaveInfo();
                }

                if (sortingOrder == 0)
                {
                    sortingOrder = GetNextSortingOrder();

                    if (scrapBase != null)
                    {
                        scrapBase.CacheItem.SortingOrder = sortingOrder;
                        scrapBase.CacheItem.SaveInfo();
                    }
                }

                formDic.Add(form.Handle, new FormData(form, sortingOrder));
            }
        }

        void FormManager_Closed(Form form)
        {
            //Console.WriteLine("Closed:" + form);

            if (form != null)
            {
                formDic.Remove(form.Handle);
            }
        }

        void FormManager_Activated(Form form)
        {
            //Console.WriteLine("Activated:" + form);

            if (formDic.TryGetValue(form.Handle, out var formData))
            {
                var sortingOrder = GetNextSortingOrder();
                formData.SortingOrder = sortingOrder;

                ScrapBase scrapBase = null;

                if (form is ScrapBase scrap)
                {
                    scrapBase = scrap;
                }
                else if (form is CompactScrap compact)
                {
                    scrapBase = compact.scrap;
                }

                if (scrapBase != null)
                {
                    scrapBase.CacheItem.SortingOrder = sortingOrder;
                    scrapBase.CacheItem.SaveInfo();
                }

                topMostFormData = formData;
            }
        }

        void FormManager_Deactivated(Form form)
        {
            //Console.WriteLine("Deactivated:" + form);
        }
    }

    public class FormData
    {
        public Form Form { set; get; }
        public int SortingOrder { set; get; }

        public bool Visible => Form.Visible;

        public bool TopMost
        {
            set => Form.TopMost = value;
            get => Form.TopMost;
        }

        public WindowInfo WindowInfo
        {
            get
            {
                if (Form == null || Form.IsDisposed)
                {
                    return WindowInfo.Empty;
                }

                return WindowManager.Instance.GetWindowInfo(Form.Handle);
            }
        }


        public FormData(Form form, int sortingOrder)
        {
            Form = form;
            SortingOrder = sortingOrder;
        }


        public override string ToString()
        {
            return WindowInfo.ToString();
        }
    }
}
