using System.ComponentModel;
using System.Threading.Tasks;
using System.Diagnostics;
using MuPDFCore;
using System.Threading;
using System.IO;
using Finn.Model;
using System;
using Avalonia.Threading;
using MuPDFCore.MuPDFRenderer;
using Avalonia.Media;
using System.Text.RegularExpressions;
using System.Linq;
using Avalonia.Collections;
using Avalonia.Controls;
using MuPDFCore.StructuredText;

namespace Finn.ViewModels
{
    public class PreviewViewModel : ViewModelBase, INotifyPropertyChanged
    {
        public PreviewViewModel() { }

        public MuPDFDocument mainPreviewFile = null;
        public MuPDFDocument MainPreviewFile
        {
            get { return mainPreviewFile; }
            set { mainPreviewFile = value; OnPropertyChanged("MainPreviewFile"); }
        }



        public bool twopageMode = false;
        public bool TwopageMode
        {
            get { return twopageMode; }
            set { twopageMode = value; OnPropertyChanged("TwopageMode"); ToggleDualView(); }
        }

        public bool linkedPageMode = true;
        public bool LinkedPageMode
        {
            get { return linkedPageMode; }
            set { linkedPageMode = value; OnPropertyChanged("LinkedPageMode"); ToggleLinkedMode(); }
        }

        private FileData currentFile = null;
        public FileData CurrentFile
        {
            get { return currentFile; }
            set { currentFile = value;  OnPropertyChanged("CurrentFile"); }
        }

        public FileData requestFile = null;
        public FileData RequestFile
        {
            get { return requestFile; }
            set { requestFile = value; OnPropertyChanged("RequestFile"); }
        }

        public int requestPage1 = 0;
        public int RequestPage1
        {
            get { return requestPage1; }
            set
            {
                if (PageInRange(value))
                {
                    requestPage1 = value;
                    SetMainPage();

                    if (LinkedPageMode)
                    {
                        RequestPage2 = RequestPage1 + 1;
                    }
                    OnPropertyChanged("RequestPage1");
                }
            }
        }

        public int requestPage2 = 0;
        public int RequestPage2
        {
            get { return requestPage2; }
            set
            {
                if (PageInRange(value))
                {
                    requestPage2 = value;
                    SetSecondaryPage();
                    OnPropertyChanged("RequestPage2");
                }
            }
        }

        public int currentPage1 = 0;
        public int CurrentPage1
        {
            get { return currentPage1; }
            set { currentPage1 = value; CurrentFile.DefaultPage = value; OnPropertyChanged("CurrentPage1"); }
        }

        public int currentPage2 = 0;
        public int CurrentPage2
        {
            get { return currentPage2; }
            set { currentPage2 = value; OnPropertyChanged("CurrentPage2"); }
        }

        public int pagecount = 0;
        public int Pagecount
        {
            get { return pagecount; }
            set { pagecount = value; OnPropertyChanged("Pagecount"); }
        }

        private bool dimmedBackground = false;
        public bool DimmedBackground
        {
            get { return dimmedBackground; }
            set { if (FileAvailable) { dimmedBackground = value; SetDimmedMode(); OnPropertyChanged("DimmedBackground"); } }
        }

        private bool fileWorkerBusy = false;
        public bool FileWorkerBusy
        {
            get { return fileWorkerBusy; }
            set { fileWorkerBusy = value; OnPropertyChanged("FileWorkerBusy"); }
        }

        private bool RenderWorkerBusy = false;

        private byte[] bytes;

        private byte[] tempbytes;

        private PDFRenderer MainRenderer;

        private PDFRenderer SecondaryRenderer;

        MuPDFContext Context = null;

        private Regex Regex = null;

        private AvaloniaList<int> searchPages = new AvaloniaList<int>() { };
        public AvaloniaList<int> SearchPages
        {
            get { return searchPages; }
            set { searchPages = value; OnPropertyChanged("SearchPages"); }
        }

        private AvaloniaList<string> searchPagesText = new AvaloniaList<string>() { };
        public AvaloniaList<string> SearchPagesText
        {
            get { return searchPagesText; }
            set { searchPagesText = value; OnPropertyChanged("SearchPagesText"); }
        }

        private AvaloniaList<FileData> recentFiles = new AvaloniaList<FileData>();
        public AvaloniaList<FileData> RecentFiles
        {
            get { return recentFiles; }
            set { recentFiles = value; OnPropertyChanged("RecentFiles"); }
        }

        private int searchPageIndex = 0;
        public int SearchPageIndex
        {
            get { return searchPageIndex; }
            set { searchPageIndex = value; SetSearchPage(); OnPropertyChanged("SearchPageIndex"); }
        }

        private int searchItems = 0;
        public int SearchItems
        {
            get { return searchItems; }
            set { searchItems = value; OnPropertyChanged("SearchItems"); }
        }

        private bool searchMode = false;
        public bool SearchMode
        {
            get { return searchMode; }
            set { searchMode = value; OnPropertyChanged("SearchMode"); }
        }

        private bool searchBusy = false;
        public bool SearchBusy
        {
            get { return searchBusy; }
            set { searchBusy = value; OnPropertyChanged("SearchBusy"); }
        }

        public double rotation = 0;
        public double Rotation
        {
            get { return rotation; }
            set { rotation = value; OnPropertyChanged("Rotation"); }
        }

        private string statusMessage = "Ready!";
        public string StatusMessage
        {
            get { return statusMessage; }
            set { statusMessage = value; OnPropertyChanged("StatusMessage"); }
        }

        private CancellationTokenSource MainCts = new CancellationTokenSource();

        private CancellationTokenSource SearchCts = new CancellationTokenSource();

        private bool FileAvailable = false;

        private int progress = 0;
        public int Progress
        {
            get { return progress; }
            set { progress = value; OnPropertyChanged("Progress"); }
        }

        public void GetRenderControl(PDFRenderer mainRenderer, PDFRenderer secondaryRenderer)
        {

            IBrush background = new SolidColorBrush(Colors.White);

            if (MainRenderer != null)
            {
                background = MainRenderer.PageBackground;
                MainRenderer.ReleaseResources();
            }

            MainRenderer = mainRenderer;
            SecondaryRenderer = secondaryRenderer;

            MainRenderer.PageBackground = background;
            SecondaryRenderer.PageBackground = background;

            if(CurrentFile != null)
            {
                SetMainPage();
            }
        }

        public void ClearRenderer()
        {

        }

        public void SetupPage(int page = 0)
        {
            requestPage1 = page; 
        }

        public async void SetFile(string? search = null)
        {
            StatusMessage = "Setting File";

            FileAvailable = false;

            MainCts.Cancel();
            MainCts = new CancellationTokenSource();

            if (!FileWorkerBusy)
            {
                FileWorkerBusy = true;

                Task SetMain = Task.Run(()=>SetMainFile());

                await Task.WhenAll(SetMain);

                if (FileAvailable && !FileWorkerBusy)
                {
                    Debug.WriteLine("Complete");
                    Dispatcher.UIThread.Invoke(() =>
                        {
                            LinkedPageMode = true;
                            SetDefaultPage();    
                            
                            if (search != null)
                            {
                                SearchMode = true;
                                Search(search);
                            }
                        }
                    );
                }
            }
        }

        public void AddRecentFile(FileData file)
        {
            if (file != null)
            {
                if (RecentFiles.Contains(file))
                {
                    RecentFiles.Remove(file);
                }

                RecentFiles.Insert(0, file);

                if (RecentFiles.Count > 20)
                {
                    RecentFiles.RemoveAt(20);
                }
            }
        }

        private async Task SetMainFile()
        {
            string path = RequestFile.Sökväg;

            bytes = null;
            bytes = await ReadBytesWithProgress(path, MainCts.Token);

            if (RequestFile.Sökväg == path && bytes != null)
            {

                await SafeDispose();
                await SetupMainFile();
               
                FileWorkerBusy = false;
                FileAvailable = true;
            }
            else
            {
                FileWorkerBusy = false;
                SetFile();
            }
        }

        private void SetDefaultPage()
        {

            if (RequestFile.DefaultPage > MainPreviewFile.Pages.Count)
            {
                RequestPage1 = 0;
            }
            else
            {
                RequestPage1 = RequestFile.DefaultPage;
            }

            OnPropertyChanged("RequestPage1");

            CurrentPage1 = RequestPage1;
            Rotation = 0;
        }

        private async Task<byte[]> ReadBytesWithProgress(string path, CancellationToken Token)
        {
            try
            {
                Progress = 0;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (Stream source = File.OpenRead(path))
                    {
                        long total = source.Length;

                        StatusMessage = "Reading Bytes: " + (total/1000000).ToString() + " Mb";

                        byte[] buffer = new byte[4096];
                        int bytesRead;

                        int steps = (int)(total / buffer.Length);
                        int leap = steps / 20;

                        int i = 0;

                        while ((bytesRead = source.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            Token.ThrowIfCancellationRequested();

                            ms.Write(buffer, 0, bytesRead);

                            if (leap > 20)
                            {
                                if (i % leap == 0)
                                {
                                    Progress = 100 * (i+1) / steps;
                                }
                            }
                            i++;
                        }
                    }
                    Progress = 0;

                    return ms.ToArray();
                }
            }
            catch
            {
                Debug.WriteLine("ReadBytesWithProgress anceled");
                return null;
            }
        }

        private async Task SetupMainFile()
        {
            Context = new MuPDFContext();
            MainPreviewFile = new MuPDFDocument(Context, bytes, InputFileTypes.PDF);

            Pagecount = MainPreviewFile.Pages.Count;
            CurrentFile = RequestFile;
        }   
     

        public async void ToggleDualView()
        {
            await Task.Delay(20);
            if (TwopageMode)
            {
                if(RequestPage1 % 2 != 0)
                {
                    requestPage1 = RequestPage1 - 1;
                }
            }

            requestPage2 = requestPage1 + 1;
            SetMainPage();

            MainRenderer.Contain();

            if (TwopageMode)
            {
                SetSecondaryPage();
                SecondaryRenderer.Contain();
            }
            
            if (!LinkedPageMode)
            {
                LinkedPageMode = true;
            }
        }


        public async Task SafeDispose()
        {
            StatusMessage = "Disposing File";
            StopSearch();
            ClearSearch();


            while (RenderWorkerBusy || SearchBusy)
            {
                Debug.WriteLine("Waiting");
                await Task.Delay(100);
            }

            if (MainPreviewFile != null)
            {
                Dispatcher.UIThread.Invoke(() =>
                {
                    DisposeHighlight();
                    MainRenderer?.ReleaseResources();
                    SecondaryRenderer?.ReleaseResources();
                    MainPreviewFile?.Dispose();
                    Context?.Dispose();
                });

                FileAvailable = false;
            }

            StatusMessage = "File Disposed";
        }


        private async void SetMainPage()
        {

            if (FileWorkerBusy || SearchBusy || !PageInRange(RequestPage1))
            {
                return;
            }

            Debug.WriteLine("Setting main page");

            MainRenderer.IsVisible = false;
            MainRenderer.HighlightedRegions = null;

            MainRenderer.Initialize(MainPreviewFile, 1, RequestPage1, 0.2);
            MainRenderer.IsVisible = true;

            SetSearchResults();
            CurrentPage1 = RequestPage1;
        }

        private async void SetSecondaryPage()
        {
            if (FileWorkerBusy || SearchBusy || !PageInRange(RequestPage2) || !TwopageMode)
            {
                return;
            }

            Debug.WriteLine("Setting secondary page");

            SecondaryRenderer.IsVisible = false;
            SecondaryRenderer.HighlightedRegions = null;

            SecondaryRenderer.Initialize(MainPreviewFile, 1, RequestPage2, 0.2);
            SecondaryRenderer.IsVisible = true;

            SetSecondarySearchResults();
            CurrentPage2 = RequestPage2;
        }



        private void SetSearchResults()
        {
            if (SearchPages != null)
            {
                if (SearchPages.Contains(RequestPage1))
                {
                    MainRenderer.Search(Regex);
                    searchPageIndex = SearchPages.IndexOf(RequestPage1);
                    OnPropertyChanged("SearchPageIndex");
                }
            }
        }

        private void SetSecondarySearchResults()
        {
            if (SearchPages != null)
            {
                if (SearchPages.Contains(RequestPage2))
                {
                    SecondaryRenderer.Search(Regex);
                    searchPageIndex = SearchPages.IndexOf(RequestPage2);
                    OnPropertyChanged("SearchPageIndex");
                }
            }
        }

        public void ToggleVisibility(bool isVisible)
        {
            if (TwopageMode)
            {
                MainRenderer.IsVisible = isVisible;
                SecondaryRenderer.IsVisible = isVisible;
            }
            else
            {
                MainRenderer.IsVisible = isVisible;
            }
        }

        public void NextPage(bool SecondPage = false)
        {
            if (!TwopageMode)
            {
                RequestPage1 = RequestPage1 + 1;
            }

            if (TwopageMode)
            {
                if (LinkedPageMode)
                {
                    RequestPage1 = RequestPage1 + 2;
                }
                else
                {
                    if (!SecondPage)
                    {
                        RequestPage1 = RequestPage1 + 1;
                    }
                    else
                    {
                        RequestPage2 = RequestPage2 + 1;
                    }
                }
            }
        }


        public void PrevPage(bool SecondPage = false)
        {
            if (!TwopageMode)
            {
                RequestPage1 = RequestPage1 - 1;
            }

            if (TwopageMode)
            {
                if (LinkedPageMode)
                {
                    RequestPage1 = RequestPage1 - 2;
                }
                else
                {
                    if (!SecondPage)
                    {
                        RequestPage1 = RequestPage1 - 1;
                    }
                    else
                    {
                        RequestPage2 = RequestPage2 - 1;
                    }
                }
            }
        }

        public bool PageInRange(int pagenr)
        {
            if (pagenr >= 0 && pagenr < Pagecount)
            {
                return true;
            }
            else
            {
                return false;
            }
        }

        public void ToggleDimmed()
        {
            DimmedBackground = !DimmedBackground;
        }

        public void SetDimmedMode()
        {

            if (DimmedBackground)
            {
                MainRenderer.PageBackground = new SolidColorBrush(Colors.AntiqueWhite);
                SecondaryRenderer.PageBackground = new SolidColorBrush(Colors.AntiqueWhite);
            }
            else
            {
                MainRenderer.PageBackground = new SolidColorBrush(Colors.White);
                SecondaryRenderer.PageBackground = new SolidColorBrush(Colors.White);
            }

            SetMainPage();

            if (TwopageMode)
            {
                SetSecondaryPage();
            }
        }

        public void ToggleLinkedMode()
        {
            if (LinkedPageMode)
            {
                if (CurrentPage1 % 2 != 0)
                {
                    RequestPage1 = CurrentPage1 - 1;
                }

                RequestPage2 = CurrentPage1 + 1;
            }

        }

        public void CopyToClipboard(Avalonia.Visual window)
        {
            if (CurrentFile != null)
            {
                string selected = MainRenderer.GetSelectedText();
                TopLevel.GetTopLevel(window).Clipboard.SetTextAsync(selected);
            }
        }

        public void Search(string text)
        {
            if (FileAvailable && text != "" && text != null)
            {
                ClearSearch();

                Regex = new Regex(text, RegexOptions.IgnoreCase);

                if (SearchPages.Count > 0)
                {
                    SearchPages.Clear();
                }

                SearchCts = new CancellationTokenSource();

                Task.Run(() => SearchDocumentAsync(SearchCts.Token));

            }
        }

        public async void SearchDocumentAsync(CancellationToken Token)
        {
            try
            {
                SearchBusy = true;
                SearchPagesText.Clear();

                for (int i = 0; i < Pagecount; i++)
                {
                    Token.ThrowIfCancellationRequested();

                    using (IDisposable disposable = MainPreviewFile.GetStructuredTextPage(i))
                    {
                        MuPDFStructuredTextPage StructuredPage = (MuPDFStructuredTextPage)disposable;

                        int nr = StructuredPage.Search(Regex).Count();

                        if (nr != 0)
                        {
                            SearchPages.Add(i);
                            SearchItems = SearchItems + 1;
                            SearchPagesText.Add("Page: " + (i + 1).ToString() + " - " + nr + " items");
                        }
                    }
                }
                SearchBusy = false;

                if (SearchItems > 0)
                {

                    Dispatcher.UIThread.Invoke(() =>
                    {
                        SearchPageIndex = 0;
                        RequestPage1 = SearchPages[SearchPageIndex];
                        MainRenderer.Search(Regex);
                    });

                }
                else
                {

                }
                
            }
            catch
            {
                SearchBusy = false;
            }

        }

        public void NextSearchPage()
        {
            if (Regex != null && SearchPages != null)
            {
                if (SearchPageIndex < SearchPages.Count - 1)
                {
                    SearchPageIndex++;
                }
            }
        }

        public void PrevSearchPage()
        {
            if (Regex != null && SearchPages != null)
            {
                if (SearchPageIndex > 0)
                {
                    SearchPageIndex--;
                }
            }
        }
        
        private void SetSearchPage()
        {
            if (SearchMode && SearchItems != 0)
            {
                RequestPage1 = SearchPages[SearchPageIndex];
            }
        }

        private void DisposeHighlight()
        {
            if (MainRenderer.HighlightedRegions != null)
            {
                MainRenderer.HighlightedRegions = null;
            }

            if (SecondaryRenderer.HighlightedRegions != null)
            {
                SecondaryRenderer.HighlightedRegions = null;
            }
        }

        public async Task StopSearch()
        {
            if (SearchBusy)
            {
                SearchCts.Cancel();

                while (SearchBusy)
                {
                    await Task.Delay(25);
                }
            }
        }


        public void ClearSearch()
        {
            SearchItems = 0;
            SearchPageIndex = 0;

            SearchPagesText.Clear();
            SearchPages.Clear();
        }


        public async Task CloseRenderer()
        {
            await StopSearch();
            ClearSearch();

            await SafeDispose();

        }
    }
}