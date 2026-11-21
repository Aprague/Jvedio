using Jvedio.Core.DataBase;
using Jvedio.Core.Enums;
using Jvedio.Entity;
using Jvedio.Mapper;
using SuperControls.Style;
using SuperUtils.CustomEventArgs;
using SuperUtils.Framework.Tasks;
using SuperUtils.IO;
using SuperUtils.Time;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using static Jvedio.MapperManager;

namespace Jvedio.Core.Scan
{
    public class ScanTask : AbstractTask
    {

        private const string DEFAULT_VIDEO_EXT = "3g2,3gp,3gp2,3gpp,amr,amv,asf,avi,bdmv,bik,d2v,divx,drc,dsa,dsm,dss,dsv,evo,f4v,flc,fli,flic,flv,hdmov,ifo,ivf,m1v,m2p,m2t,m2ts,m2v,m4b,m4p,m4v,mkv,mp2v,mp4,mp4v,mpe,mpeg,mpg,mpls,mpv2,mpv4,mov,mts,ogm,ogv,pss,pva,qt,ram,ratdvd,rm,rmm,rmvb,roq,rpm,smil,smk,swf,tp,tpr,ts,vob,vp6,webm,wm,wmp,wmv";
        private const string DEFAULT_IMAGE_EXT = "png,jpg,jpeg,bmp,jpe,ico,gif";


        #region "事件"

        public event EventHandler onScanning;



        #endregion

        #region "静态属性"


        public static Dictionary<NotImportReason, string> ReasonToString { get; set; } = new Dictionary<NotImportReason, string>()
        {
            { NotImportReason.NotInExtension, LangManager.GetValueByKey("NotSupportedExt") },
            { NotImportReason.SizeTooSmall, LangManager.GetValueByKey("FileSizeTooSmall") },
            { NotImportReason.SizeTooLarge, LangManager.GetValueByKey("FileSizeTooBig") },
        };


        private static string[] NFOUpdateMetaProps { get; set; } = new string[]
        {
            "Title",
            "ReleaseYear",
            "ReleaseDate",
            "Country",
            "Genre",
            "Rating",
            "LastScanDate",
            "PathExist",
        };

        private static string[] NFOUpdateVideoProps { get; set; } = new string[]
        {
            "Plot",
            "Director",
            "Duration",
            "Studio",
            "Series",
            "Outline",
        };

        #endregion

        #region "属性"

        public static string VIDEO_EXTENSIONS { get; set; }

        public static string PICTURE_EXTENSIONS { get; set; }

        public static List<string> VIDEO_EXTENSIONS_LIST { get; set; }

        public static List<string> PICTURE_EXTENSIONS_LIST { get; set; }

        public ScanResult ScanResult { get; set; }

        /// <summary>
        /// 需要扫描的目录
        /// </summary>
        public List<string> ScanPaths { get; set; }

        public List<string> FilePaths { get; set; }

        public List<string> FileExt { get; set; }

        private List<ActorInfo> existActors { get; set; }
        #endregion



        static ScanTask()
        {
            VIDEO_EXTENSIONS = DEFAULT_VIDEO_EXT;
            PICTURE_EXTENSIONS = DEFAULT_IMAGE_EXT;
            VIDEO_EXTENSIONS_LIST = VIDEO_EXTENSIONS.Split(',').Select(arg => "." + arg).ToList();
            PICTURE_EXTENSIONS_LIST = PICTURE_EXTENSIONS.Split(',').Select(arg => "." + arg).ToList();
            STATUS_TO_TEXT_DICT[TaskStatus.Running] = $"{LangManager.GetValueByKey("Scanning")}...";
        }


        public ScanTask(List<string> scanPaths, List<string> filePaths,
            IEnumerable<string> fileExt = null) : base()
        {
            if (scanPaths != null && scanPaths.Count > 0)
                ScanPaths = scanPaths.Where(arg => Directory.Exists(arg)).ToList();
            if (filePaths != null && filePaths.Count > 0)
                FilePaths = filePaths.Where(arg => File.Exists(arg)).ToList();
            if (fileExt != null) {
                FileExt = new List<string>();
                foreach (var item in fileExt) {
                    string ext = item.Trim();
                    if (string.IsNullOrEmpty(ext))
                        continue;
                    if (!item.StartsWith("."))
                        FileExt.Add("." + ext);
                    else
                        FileExt.Add(ext);
                }
            }

            if (ScanPaths == null)
                ScanPaths = new List<string>();
            if (FilePaths == null)
                FilePaths = new List<string>();
            if (FileExt == null)
                FileExt = VIDEO_EXTENSIONS_LIST; // 默认导入视频
            ScanResult = new ScanResult();
            Logs.Add($"scan task init, paths count: {FilePaths.Count}, dir count: {ScanPaths.Count}");
        }

        private void ScanDir()
        {
            int total = ScanPaths.Count;
            int count = 0;
            object lockObj = new object();
            Parallel.ForEach(ScanPaths, (path) => {
                if (!Directory.Exists(path))
                    return;
                IEnumerable<string> paths = DirHelper.GetFileList(path, "*.*", (ex) => {
                    // 发生异常
                    Logger.Error(ex.Message);
                }, (dir) => {
                    Message = dir;
                    onScanning?.Invoke(this, new MessageCallBackEventArgs(dir));
                }, TokenCTS);
                lock (lockObj) {
                    FilePaths.AddRange(paths);
                    count++;
                    Progress = (float)Math.Round(((float)count) / total, 4) * 100;
                }
            });
            Progress = 30;
        }

        /// <summary>
        /// 在 UI 中显示要导入的文件
        /// </summary>
        private void NotifyScanPath()
        {
            if (ScanPaths.Count == 0 && FilePaths.Count != 0) {
                string path = FilePaths[FilePaths.Count - 1];
                Message = path;
                onScanning?.Invoke(this, new MessageCallBackEventArgs(path));
            }
        }

        public override void DoWork()
        {
            Logger.Info(LangManager.GetValueByKey("BeginScan"));
            Task.Run(() => {
                StartWatch();
                NotifyScanPath();
                ScanDir();

                try {
                    CheckStatus();
                } catch (TaskCanceledException) {
                    FinalizeWithCancel();
                    return;
                }

                VideoParser videoParser = new VideoParser((msg) => {
                    if (Logs != null)
                        Logs.Add(msg);
                });

                try {
                    (List<Video> import, Dictionary<string, NotImportReason> notImport, List<string> failNFO)
                    parseResult = videoParser.ParseMovie(FilePaths, FileExt, Token, ConfigManager.ScanConfig.ScanNfo, callBack: (msg) => {
                        Logger.Error(msg);
                    });

                    Progress = 60;

                    try {
                        CheckStatus();
                    } catch (TaskCanceledException ex) {
                        Logger.Error(ex.Message);
                        base.FinalizeWithCancel();
                        base.OnCompleted(null);
                        return;
                    }

                    // 将 NFO 提取出来
                    List<Video> importNFO = parseResult.import.Where(arg => arg.Path.ToLower().EndsWith(".nfo")).ToList();
                    parseResult.import.RemoveAll(arg => arg.Path.ToLower().EndsWith(".nfo"));

                    // 一次性构建 VID 索引（数据库 + 新导入），避免 HandleImportNFO 查不到刚插入的视频
                    Dictionary<string, Video> vidIndex = BuildVidIndex();
                    HandleImport(parseResult.import, vidIndex);

                    Progress = 70;

                    HandleImportNFO(importNFO, vidIndex);         // 导入视频后再导入 NFO

                    Progress = 80;

                    HandleNotImport(parseResult.notImport);

                    Progress = 90;

                    HandleFailNFO(parseResult.failNFO);

                    // 设置总数（包含跳过的重复）
                    ScanResult.TotalCount = ScanResult.Import.Count + ScanResult.NotImport.Count + ScanResult.FailNFO.Count + ScanResult.Skipped;

                    Progress = 100;
                    Success = true;
                } catch (Exception ex) {
                    Logger.Error(ex.Message);
                    FinalizeWithCancel();
                    return;
                }

                Running = false;
                StopWatch();
                ScanResult.ElapsedMilliseconds = ElapsedMilliseconds;
                Status = TaskStatus.RanToCompletion;
                base.OnCompleted(null);
            });
        }

        private void HandleImport(List<Video> import, Dictionary<string, Video> nfoMatchIndex)
        {
            if (import == null || import.Count == 0) {
                return;
            }

            // 构建路径+大小索引（来自数据库），用于精确重复检测
            // 注意：必须从 metadata 表查询，metadata_video 表不含 Path/Size 字段
            List<MetaData> existData = metaDataMapper.SelectList();

            Dictionary<string, long> pathSizeIndex = new Dictionary<string, long>();
            foreach (MetaData m in existData) {
                // 只查视频类型（DataType=0）
                if (m.DataType == Core.Enums.DataType.Video && !string.IsNullOrEmpty(m.Path) && m.Size > 0) {
                    string key = m.Path + "|||" + m.Size;
                    pathSizeIndex[key] = m.Size;
                }
            }

            List<Video> toImport = new List<Video>();
            foreach (Video video in import) {
                // 完全重复 = 路径+后缀+大小 全部相同
                string key = video.Path + "|||" + video.Size;
                if (pathSizeIndex.ContainsKey(key)) {
                    if (Logs != null) {
                        Logs.Add($"跳过重复: {video.Path}");
                    }
                    ScanResult.Skipped++;
                    continue;
                }
                toImport.Add(video);
            }

            if (toImport.Count == 0) {
                if (Logs != null) {
                    Logs.Add($"全部重复，跳过导入");
                }
                return;
            }

            if (Logs != null) {
                Logs.Add($"新导入 {toImport.Count} 个，跳过 {ScanResult.Skipped} 个");
            }

            // 将通过的记录加入索引，避免同批导入中的重复也被导入
            foreach (Video video in toImport) {
                string key = video.Path + "|||" + video.Size;
                pathSizeIndex[key] = video.Size;
            }

            // 同时更新 NFO 匹配索引（供 HandleImportNFO 使用）
            foreach (Video video in toImport) {
                if (!string.IsNullOrEmpty(video.VID)) {
                    nfoMatchIndex[video.VID] = video;
                }
            }

            ScanResult.Import.AddRange(toImport.Select(v => v.Path));
            ScanResult.InsertVideos.AddRange(toImport);
            InsertData(toImport);
        }

        /// <summary>
        /// 构建 VID→Video 索引（数据库中的视频 + 用于后续 NFO 匹配）
        /// </summary>
        private Dictionary<string, Video> BuildVidIndex()
        {
            Dictionary<string, Video> index = new Dictionary<string, Video>();
            List<Video> existVideos = videoMapper.SelectList();
            Logger.Info($"[BuildVidIndex] 数据库中共 {existVideos?.Count ?? 0} 条视频");
            if (existVideos != null) {
                foreach (Video v in existVideos) {
                    if (!string.IsNullOrEmpty(v.VID)) {
                        index[v.VID] = v;
                    }
                }
            }
            Logger.Info($"[BuildVidIndex] 索引中共 {index.Count} 个不同 VID");
            return index;
        }

        private void HandleNotImport(Dictionary<string, NotImportReason> notImport)
        {
            foreach (var key in notImport.Keys) {
                if (ScanResult.NotImport.ContainsKey(key))
                    continue;
                NotImportReason reason = notImport[key];
                ScanResult.NotImport.Add(key, new ScanDetailInfo(ReasonToString[reason]));
            }
        }

        private void HandleFailNFO(List<string> failNFO)
        {
            if (failNFO == null || failNFO.Count == 0)
                return;
            foreach (string path in failNFO)
                ScanResult.FailNFO.Add(path);
        }

        private void CopyNfoImage(Dictionary<string, string> dict, List<Video> import, ImageType imageType)
        {
            if (dict == null)
                return;

            switch (imageType) {
                case ImageType.Big:
                    if (dict.ContainsKey("BigImagePath") && !string.IsNullOrEmpty(dict["BigImagePath"])) {
                        string path = dict["BigImagePath"];
                        string dirname = path.ToLower();
                        CopyImage(import, dirname, imageType);
                    }
                    break;
                case ImageType.Small:
                    if (dict.ContainsKey("SmallImagePath") && !string.IsNullOrEmpty(dict["SmallImagePath"])) {
                        string path = dict["SmallImagePath"];
                        string dirname = path.ToLower();
                        CopyImage(import, dirname, imageType);
                    }
                    break;
                case ImageType.Actor:
                case ImageType.Preview:
                case ImageType.ScreenShot:
                    CopyImages(import, imageType);
                    break;
                default:

                    break;
            }
        }


        private string GetImagePathByType(Video video, ImageType type)
        {
            switch (type) {
                case ImageType.Big:
                    return video.GetBigImage();
                case ImageType.Small:
                    return video.GetSmallImage();
                case ImageType.ScreenShot:
                    return video.GetScreenShot();
                case ImageType.Preview:
                    return video.GetExtraImage();
                case ImageType.Actor:
                    return video.GetBigImage();
            }
            return "";
        }

        private void CopyImage(List<Video> import, string dirName, ImageType imageType)
        {
            if (string.IsNullOrEmpty(dirName))
                return;

            foreach (Video item in import) {
                if (string.IsNullOrEmpty(item.Path))
                    continue;
                string dir = Path.GetDirectoryName(item.Path);
                if (!Directory.Exists(dir))
                    continue;
                string[] arr = FileHelper.TryGetAllFiles(dir, "*.*");
                if (arr == null || arr.Length == 0)
                    continue;
                List<string> list = arr.ToList();
                list = list.Where(arg => ScanTask.PICTURE_EXTENSIONS_LIST
                                    .Contains(System.IO.Path.GetExtension(arg).ToLower())).ToList();
                string filename = Path.GetFileName(dirName);
                string originPath = list.Where(arg =>
                    Path.GetFileNameWithoutExtension(arg).ToLower().IndexOf(filename) >= 0).FirstOrDefault();
                if (File.Exists(originPath)) {
                    string targetImagePath = GetImagePathByType(item, imageType);
                    if (!File.Exists(targetImagePath)) {
                        targetImagePath = Path.Combine(Path.GetDirectoryName(targetImagePath),
                            Path.GetFileNameWithoutExtension(targetImagePath) + Path.GetExtension(originPath));
                        FileHelper.TryCopyFile(originPath, targetImagePath, true);
                    } else if (ConfigManager.ScanConfig.CopyNFOOverwriteImage) {
                        FileHelper.TryCopyFile(originPath, targetImagePath, true);
                    }
                }
            }
        }

        private void CopyImages(List<Video> import, ImageType type)
        {
            foreach (Video item in import) {
                if (string.IsNullOrEmpty(item.Path))
                    continue;
                string dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(item.Path), ConfigManager.ScanConfig.CopyNFOPreviewPath));
                if (type == ImageType.ScreenShot)
                    dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(item.Path), ConfigManager.ScanConfig.CopyNFOScreenShotPath));
                else if (type == ImageType.Actor)
                    dir = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(item.Path), ConfigManager.ScanConfig.CopyNFOActorPath));
                if (!Directory.Exists(dir))
                    continue;
                string[] arr = FileHelper.TryGetAllFiles(dir, "*.*");
                if (arr == null || arr.Length == 0)
                    continue;
                List<string> list = arr.ToList();
                list = list.Where(arg => ScanTask.PICTURE_EXTENSIONS_LIST.Contains(System.IO.Path.GetExtension(arg).ToLower())).ToList();
                // 预览图目录
                string targetPath = item.GetExtraImage();
                if (type == ImageType.ScreenShot)
                    targetPath = item.GetScreenShot();
                else if (type == ImageType.Actor)
                    targetPath = item.GetActorPath();
                DirHelper.TryCreateDirectory(targetPath);
                foreach (var path in list) {
                    string targetFilePath = Path.Combine(targetPath, Path.GetFileName(path));
                    FileHelper.TryCopyFile(path, targetFilePath, ConfigManager.ScanConfig.CopyNFOOverwriteImage);
                }

            }
        }

        private void HandleImportNFO(List<Video> import, Dictionary<string, Video> vidIndex)
        {
            if (import == null || import.Count <= 0)
                return;

            existActors = actorMapper.SelectList();

            // 构建演员名称快速查找索引（避免 HandleActor 内重复构建）
            Dictionary<string, ActorInfo> actorDict = null;
            if (existActors != null) {
                actorDict = existActors.ToDictionary(a => a.ActorName, StringComparer.OrdinalIgnoreCase);
            }

            // 解析图片路径

            Dictionary<string, object> picPaths = ConfigManager.Settings.PicPaths;
            if (picPaths != null && picPaths.ContainsKey(PathType.RelativeToData.ToString())) {
                Dictionary<string, string> dict = null;
                try {
                    dict = (Dictionary<string, string>)picPaths[PathType.RelativeToData.ToString()];
                } catch (Exception ex) {
                    Logger.Error(ex.Message);
                }

                // 复制图片
                if (ConfigManager.ScanConfig.CopyNFOPicture) {
                    CopyNfoImage(dict, import, ImageType.Big);
                    CopyNfoImage(dict, import, ImageType.Small);
                }

                if (ConfigManager.ScanConfig.CopyNFOActorPicture)
                    CopyNfoImage(dict, import, ImageType.Actor);

                if (ConfigManager.ScanConfig.CopyNFOPreview)
                    CopyNfoImage(dict, import, ImageType.Preview);

                if (ConfigManager.ScanConfig.CopyNFOScreenShot)
                    CopyNfoImage(dict, import, ImageType.ScreenShot);
            }

            // 1. 匹配已存在的视频，更新元数据（使用共享索引，包含刚导入的视频）
            List<Video> toUpdate = new List<Video>();
            foreach (Video video in import) {
                Video existVideo;
                if (!string.IsNullOrEmpty(video.VID) && vidIndex.TryGetValue(video.VID, out existVideo)) {
                    video.DataID = existVideo.DataID;
                    video.MVID = existVideo.MVID; // 下面使用 videoMapper 更新的时候会使用到
                    ScanResult.Update.Add(video.Path, LangManager.GetValueByKey("UpdateNFO"));
                    video.Path = null;
                    video.PathExist = 1;
                    video.LastScanDate = DateHelper.Now();
                    toUpdate.Add(video);
                    HandleActor(video, actorDict);
                }
            }

            import.RemoveAll(arg => !string.IsNullOrEmpty(arg.VID) && vidIndex.ContainsKey(arg.VID));

            if (toUpdate?.Count > 0)
                videoMapper.UpdateBatch(toUpdate, NFOUpdateVideoProps);
            List<MetaData> toUpdateData = toUpdate.Select(arg => arg.toMetaData()).ToList();
            if (toUpdateData?.Count > 0)
                metaDataMapper.UpdateBatch(toUpdateData, NFOUpdateMetaProps);

            // 2. 剩下的都是需要导入的
            InsertData(import);
        }

        private void HandleActor(Video video, Dictionary<string, ActorInfo> cachedActorDict = null)
        {
            // 更新演员 — 使用 Name 快速索引，避免重复 O(n) 搜索
            if (string.IsNullOrEmpty(video.ActorNames))
                return;

            // 优先使用外部传入的缓存字典，否则自行构建
            Dictionary<string, ActorInfo> actorDict;
            if (cachedActorDict != null) {
                actorDict = cachedActorDict;
            } else if (existActors != null) {
                actorDict = existActors.ToDictionary(a => a.ActorName, StringComparer.OrdinalIgnoreCase);
            } else {
                return;
            }

            List<string> names = video.ActorNames.Split(SuperUtils.Values.ConstValues.Separator).ToList();
            List<string> urls = video.ActorThumbs;

            foreach (string name in names) {
                if (string.IsNullOrEmpty(name))
                    continue;
                string url = string.Empty;
                int idx = names.IndexOf(name);
                if (idx < urls.Count)
                    url = urls[idx];

                if (!actorDict.TryGetValue(name, out ActorInfo actorInfo) || actorInfo.ActorID <= 0) {
                    // 新演员
                    actorInfo = new ActorInfo { ActorName = name, ImageUrl = url };
                    actorMapper.Insert(actorInfo);
                    if (existActors != null) existActors.Add(actorInfo);
                    if (cachedActorDict == null) actorDict[name] = actorInfo;
                    else cachedActorDict[name] = actorInfo;
                } else {
                    // 已有演员，更新图片 URL
                    actorInfo.ImageUrl = url;
                    actorMapper.UpdateFieldById("ImageUrl", url, actorInfo.ActorID);
                }

                // 保存演员-视频关联
                string sql = $"insert or ignore into metadata_to_actor (ActorID,DataID) values ({actorInfo.ActorID},{video.DataID})";
                metaDataMapper.ExecuteNonQuery(sql);
            }
        }

        private void InsertData(ICollection<Video> toInsert)
        {
            foreach (Video video in toInsert) {
                video.DBId = ConfigManager.Main.CurrentDBId;
                video.FirstScanDate = DateHelper.Now();
                video.LastScanDate = DateHelper.Now();
                video.PathExist = 1;
                ScanResult.Import.Add(video.Path);
            }

            List<MetaData> toInsertData = toInsert.Select(arg => arg.toMetaData()).ToList();
            if (toInsertData.Count <= 0)
                return;

            // 批量插入元数据（单条后获取ID，其余批量）
            long.TryParse(metaDataMapper.InsertAndGetID(toInsertData[0]).ToString(), out long before);
            toInsertData.RemoveAt(0);

            try {
                metaDataMapper.ExecuteNonQuery("BEGIN TRANSACTION;"); // 开启事务，这样子其他线程就不能更新
                metaDataMapper.InsertBatch(toInsertData);
                SqlManager.DataBaseBusy = true;
            } catch (Exception ex) {
                Logger.Error(ex.Message);
                OnError(new MessageCallBackEventArgs(ex.Message));
            } finally {
                metaDataMapper.ExecuteNonQuery("END TRANSACTION;");
                SqlManager.DataBaseBusy = false;
            }

            // 处理 DataID
            foreach (Video video in toInsert) {
                video.DataID = before;
                before++;
            }

            try {
                videoMapper.ExecuteNonQuery("BEGIN TRANSACTION;"); // 开启事务，这样子其他线程就不能更新
                SqlManager.DataBaseBusy = true;
                videoMapper.InsertBatch(toInsert);
            } catch (Exception ex) {
                Logger.Error(ex.Message);
                OnError(new MessageCallBackEventArgs(ex.Message));
            } finally {
                videoMapper.ExecuteNonQuery("END TRANSACTION;");
                SqlManager.DataBaseBusy = false;
            }

            AddTags(toInsert);

            // 处理演员 — 一次性预加载 + 构建字典，避免 foreach 内重复构建
            existActors = actorMapper.SelectList();
            Dictionary<string, ActorInfo> insertActorDict = null;
            if (existActors != null) {
                insertActorDict = existActors.ToDictionary(a => a.ActorName, StringComparer.OrdinalIgnoreCase);
            }
            foreach (var video in toInsert)
                HandleActor(video, insertActorDict);

            ScanResult.InsertVideos.AddRange(toInsert);
        }

        private void AddTags(ICollection<Video> videos)
        {
            // 处理标记
            List<string> list = new List<string>();
            foreach (Video video in videos) {
                // 高清
                if (video.IsHDV())
                    list.Add($"({video.DataID},1)");

                // 中文
                if (video.IsCHS())
                    list.Add($"({video.DataID},2)");

                // 新加入
                // todo 由于设计之初未考虑到多的空余位置添加标记，因此新标记以 ID 10000开头
                list.Add($"({video.DataID},10000)");
            }

            if (list.Count > 0) {
                string sql = $"insert or ignore into metadata_to_tagstamp (DataID,TagID) values {string.Join(",", list)}";
                videoMapper.ExecuteNonQuery(sql);
            }
        }

        public void CheckStatus()
        {
            if (Status == TaskStatus.Canceled) {
                StopWatch();
                Running = false;
                throw new TaskCanceledException();
            }
        }
    }
}
