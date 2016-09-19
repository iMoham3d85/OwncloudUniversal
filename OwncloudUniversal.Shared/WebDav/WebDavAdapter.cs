﻿using OwncloudUniversal.Model;
using OwncloudUniversal.Shared.Model;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Windows.Storage;

namespace OwncloudUniversal.Shared.WebDav
{
    public class WebDavAdapter : AbstractAdapter
    {
        public override async Task<AbstractItem> AddItem(AbstractItem localItem)
        {
            AbstractItem targetItem = null;
            if (localItem.IsCollection)
            {
                //build path and folder name
                string path = _BuildRemoteFolderPath(localItem.Association, localItem.EntityId);
                var folderName = (await StorageFolder.GetFolderFromPathAsync(localItem.EntityId)).DisplayName;

                //create folder and parent folders
                await CreateFolder(localItem.Association, localItem, folderName);
                //load the folder again to update the item properties
                var folder = await ConnectionManager.GetFolder(path);
                targetItem = folder.FirstOrDefault(x => x.DavItem.DisplayName == folderName);
                targetItem.Association = localItem.Association;
            }
            else
            {
                StorageFile file = await StorageFile.GetFileFromPathAsync(localItem.EntityId);
                var folderPath = _BuildRemoteFolderPath(localItem.Association, file.Path);
                try
                {
                    //if the file already exists dont upload it again
                    var folder = await ConnectionManager.GetFolder(folderPath);
                    var existingItem = folder.Where(x => x.DavItem.DisplayName == file.Name).FirstOrDefault();
                    if (existingItem != null)
                    {
                        existingItem.Association = localItem.Association;
                        return existingItem;
                    }
                }
                catch (Exception)
                {
                    
                }
                using (var stream = await file.OpenStreamForReadAsync())
                {
                    await CreateFolder(localItem.Association, localItem, Path.GetDirectoryName(file.Path));
                    await ConnectionManager.Upload(folderPath, stream, file.Name);
                    var folder = await ConnectionManager.GetFolder(folderPath);
                    targetItem = folder.FirstOrDefault(x => x.DavItem.DisplayName == file.Name);
                }


                targetItem.Association = localItem.Association;
            }
            return targetItem;
        }

        private async Task CreateFolder(FolderAssociation association, AbstractItem localItem, string name)
        {
            //adds the folder and if necessesary the parent folder
            var remoteBaseFolder = GetAssociatedItem(association.RemoteFolderId).EntityId;
            var path = _BuildRemoteFolderPath(association, localItem.EntityId).Replace(remoteBaseFolder, "");
            var folders = path.Split('/');
            if (localItem.IsCollection)
            {
                folders = (path + "/" + name).Split('/');
            }
            

            var currentFolder = remoteBaseFolder.TrimEnd('/');
            for (int i = 0; i < folders.Length; i++)
            {
                try
                {
                    var folderContent = await ConnectionManager.GetFolder(currentFolder);
                    if(folderContent.Count(x => x.DavItem.DisplayName == folders[i] && x.IsCollection) == 0)
                        if(!string.IsNullOrWhiteSpace(folders[i]))
                            await ConnectionManager.CreateFolder(currentFolder, folders[i]);
                }
                catch (Exception e)
                {
                    System.Diagnostics.Debug.WriteLine(e.Message);
                    //wenn ordner schon existiert weiter machen
                }
                currentFolder += '/' + folders[i];
            }
        }

        public override async Task<AbstractItem> UpdateItem(AbstractItem item)
        {
            return await AddItem(item);
        }

        public override async Task DeleteItem(AbstractItem item)
        {
            var _item = (LocalItem)item;
            if (_item.IsCollection)
            {
                string path = _BuildRemoteFolderPath(_item.Association, _item.EntityId);
                string name = (await StorageFolder.GetFolderFromPathAsync(_item.EntityId)).DisplayName;
                ConnectionManager.DeleteFolder(_item.EntityId + '/'+ name);
                return;
            }
            else
            {
                ConnectionManager.DeleteFile(_BuildRemoteFilePath(_item.Association, _item.EntityId));
            }
        }

        public override async Task<List<AbstractItem>> GetAllItems(FolderAssociation association)
        {
            List<AbstractItem> items = new List<AbstractItem>();
            var remoteFolder = GetAssociatedItem(association.RemoteFolderId);
            await _CheckRemoteFolderRecursive(remoteFolder, items);
            return items;
        }
        private async Task _CheckRemoteFolderRecursive(AbstractItem folder, List<AbstractItem> result)
        {
            List<RemoteItem> items = await ConnectionManager.GetFolder(folder.EntityId);
            foreach (RemoteItem item in items)
            {
                if (item.IsCollection)
                    await _CheckRemoteFolderRecursive(item, result);
                result.Add(item);
            }
        }
        private string _BuildRemoteFilePath(FolderAssociation association, string path)
        {
            var localFolder = GetAssociatedItem(association.LocalFolderId);
            Uri baseUri = new Uri(localFolder.EntityId);
            Uri fileUri = new Uri(path);
            Uri relativeUri = baseUri.MakeRelativeUri(fileUri);
            string uri = relativeUri.ToString();
            var relativeString = uri.Substring(uri.IndexOf('/') + 1);
            var remoteFolder = GetAssociatedItem(association.RemoteFolderId);
            return remoteFolder.EntityId + relativeString;
        }
        private string _BuildRemoteFolderPath(FolderAssociation association, string path)
        {
            var localFolder = GetAssociatedItem(association.LocalFolderId);
            Uri baseUri = new Uri(localFolder.EntityId);
            Uri fileUri = new Uri(path);
            var relative = fileUri.ToString().Replace(baseUri.ToString(), "");
            relative = relative.Remove(relative.LastIndexOf('/')).TrimStart('/');
            //var relativeString = uri.Remove(uri.ToString());
            var remoteFolder = GetAssociatedItem(association.RemoteFolderId);
            var result = remoteFolder.EntityId + relative;
            return result;
        }
        private AbstractItem GetAssociatedItem(long id)
        {
            return AbstractItemTableModel.GetDefault().GetItem(id);
        }
    }
}