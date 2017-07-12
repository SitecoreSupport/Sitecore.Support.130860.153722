
using Sitecore.Abstractions;
using Sitecore.Collections;
using Sitecore.Common;
using Sitecore.ContentSearch;
using Sitecore.ContentSearch.Abstractions;
using Sitecore.Data;
using Sitecore.Data.Fields;
using Sitecore.Data.Items;
using Sitecore.Data.LanguageFallback;
using Sitecore.Data.Managers;
using Sitecore.Globalization;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

namespace Sitecore.Support.ContentSearch
{
    public class SitecoreItemCrawler : Sitecore.ContentSearch.SitecoreItemCrawler
    {
        public SitecoreItemCrawler()
        {
        }

        public override void Delete(IProviderUpdateContext context, IIndexableUniqueId indexableUniqueId, IndexingOptions indexingOptions = 0)
        {
            if (base.ShouldStartIndexing(indexingOptions))
            {
                object[] parameters = new object[] { base.index.Name, indexableUniqueId };
                context.Index.Locator.GetInstance<Abstractions.IEvent>().RaiseEvent("indexing:deleteitem", parameters);
                base.Operations.Delete(indexableUniqueId, context);
                IEnumerable<SitecoreIndexableItem> enumerable2 = from i in this.GetIndexablesToUpdateOnDelete(indexableUniqueId).Select<IIndexableUniqueId, SitecoreIndexableItem>(new Func<IIndexableUniqueId, SitecoreIndexableItem>(this.GetIndexable))
                                                                 where i != null
                                                                 select i;
                IndexEntryOperationContext operationContext = new IndexEntryOperationContext
                {
                    NeedUpdateAllLanguages = false,
                    NeedUpdateAllVersions = false,
                    NeedUpdateChildren = false
                };
                this.DoUpdateFallbackField(context, indexableUniqueId);
                foreach (SitecoreIndexableItem item in enumerable2)
                {
                    this.DoUpdate(context, item, operationContext);
                }
            }
        }



        private void DoUpdateFallbackField(IProviderUpdateContext context, IIndexableUniqueId indexableUniqueId)
        {
            SitecoreIndexableItem indexableItem = this.GetIndexable(indexableUniqueId);
            if (indexableItem != null)
            {
                indexableItem.Item.Fields.ReadAll();
                if (indexableItem.Item.Fields.Any<Field>(e => e.SharedLanguageFallbackEnabled))
                {
                    IEnumerable<Item> enumerable = Sitecore.Data.Managers.LanguageFallbackManager.GetDependentLanguages(indexableItem.Item.Language, indexableItem.Item.Database, indexableItem.Item.ID).SelectMany<Language, Item>(delegate (Language language) {
                        Item item = indexableItem.Item.Database.GetItem(indexableItem.Item.ID, language);
                        return (item != null) ? ((IEnumerable<Item>)item.Versions.GetVersions()) : ((IEnumerable<Item>)new Item[0]);
                    });
                    foreach (Item item in enumerable)
                    {
                        SitecoreItemUniqueId id = new SitecoreItemUniqueId(item.Uri);
                        this.Update(context, id, IndexingOptions.Default);
                    }
                }
            }
        }


        protected override void UpdateItemVersion(IProviderUpdateContext context, Item version, IndexEntryOperationContext operationContext)
        {
            SitecoreIndexableItem indexable = this.PrepareIndexableVersion(version, context);
            base.Operations.Update(indexable, context, context.Index.Configuration);
            this.UpdateClones(context, indexable);
            this.UpdateLanguageFallbackDependentItems(context, indexable, operationContext);
        }

        internal SitecoreIndexableItem PrepareIndexableVersion(Item item, IProviderUpdateContext context)
        {
            SitecoreIndexableItem item2 = item;
            IIndexableBuiltinFields fields = item2;
            fields.IsLatestVersion = item.Versions.IsLatestVersion();
            item2.IndexFieldStorageValueFormatter = context.Index.Configuration.IndexFieldStorageValueFormatter;
            return item2;
        }


        private void UpdateClones(IProviderUpdateContext context, SitecoreIndexableItem versionIndexable)
        {
            IEnumerable<Item> clones;
            using (new WriteCachesDisabler())
            {
                clones = versionIndexable.Item.GetClones(false);
            }
            foreach (Item item in clones)
            {
                SitecoreIndexableItem indexable = this.PrepareIndexableVersion(item, context);
                if (!this.IsExcludedFromIndex(item, false))
                {
                    base.Operations.Update(indexable, context, context.Index.Configuration);
                }
            }
        }

        
        protected void UpdateLanguageFallbackDependentItems(IProviderUpdateContext context, SitecoreIndexableItem versionIndexable, IndexEntryOperationContext operationContext)
        {
            if ((operationContext != null) && !operationContext.NeedUpdateAllLanguages)
            {
                Item item = versionIndexable.Item;
                bool? currentValue = Switcher<bool?, LanguageFallbackFieldSwitcher>.CurrentValue;
                bool flag2 = true;
                if (((currentValue.GetValueOrDefault() == flag2) ? !currentValue.HasValue : true) && !base.Index.EnableFieldLanguageFallback)
                {
                    currentValue = Switcher<bool?, LanguageFallbackItemSwitcher>.CurrentValue;
                    flag2 = true;
                    if (((currentValue.GetValueOrDefault() == flag2) ? !currentValue.HasValue : true) || (StandardValuesManager.IsStandardValuesHolder(item) && (item.Fields[FieldIDs.EnableItemFallback].GetValue(false) != "1")))
                    {
                        return;
                    }
                    using (new LanguageFallbackItemSwitcher(false))
                    {
                        if (item.Fields[FieldIDs.EnableItemFallback].GetValue(true, true, false) != "1")
                        {
                            return;
                        }
                    }
                }
                if (item.Versions.IsLatestVersion())
                {
                    (from item1 in this.GetItem(item) select this.PrepareIndexableVersion(item1, context)).ToList<SitecoreIndexableItem>().ForEach(sitecoreIndexableItem => this.Operations.Update(sitecoreIndexableItem, context, context.Index.Configuration));
                }
            }
        }

        protected IEnumerable<Item> GetItem(Item item) =>
          (from item1 in LanguageFallbackManager.GetDependentLanguages(item.Language, item.Database, item.ID).SelectMany<Language, Item>(delegate (Language language) {
              Item item1 = item.Database.GetItem(item.ID, language);
              if (item1 == null)
              {
                  return new Item[0];
              }
              if (item1.IsFallback)
              {
                  return new Item[] { item1 };
              }
              return item1.Versions.GetVersions();
          })
           where !this.IsExcludedFromIndex(item1, false)
           select item1);

    }
}
