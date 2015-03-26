﻿using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using Xbim.COBieLiteUK;
using Xbim.IO;
using Xbim.ModelGeometry.Scene;
using Xbim.WindowsUI.DPoWValidation.Commands;
using Xbim.WindowsUI.DPoWValidation.Models;
using Xbim.XbimExtensions;
using XbimExchanger.IfcToCOBieLiteUK;
using XbimGeometry.Interfaces;
using cobieUKValidation = Xbim.CobieLiteUK.Validation;

namespace Xbim.WindowsUI.DPoWValidation.ViewModels
{
    class ValidationViewModel: INotifyPropertyChanged
    {
        public SelectFileCommand SelectRequirement { get; set; }
        public SelectFileCommand SelectSubmission { get; set; }

        public ValidateCommand Validate { get; set; }

        public bool IsWorking { get; set; }

        public bool FilesCanChange
        {
            get { return !IsWorking; }
        }

        public string RequirementFileSource
        {
            get { return RequirementFileInfo.File; }
            set
            {
                RequirementFileInfo.File = value;
                Validate.ChangesHappened();
            }
        }

        private Facility _requirementFacility;

        internal Facility RequirementFacility
        {
            get { return _requirementFacility; }
            set
            {
                _requirementFacility = value; 
                RequirementFacilityVM = new DPoWFacilityViewModel(_requirementFacility);
                
                if (PropertyChanged == null)
                    return;
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(@"RequirementFacilityVM"));
            }
        }


        public DPoWFacilityViewModel RequirementFacilityVM { get; private set; }

        private Facility _submissionFacility;

        internal Facility SubmissionFacility
        {
            get { return _submissionFacility; }
            set
            {
                _submissionFacility = value;
                SubmissionFacilityVM = new DPoWFacilityViewModel(_submissionFacility);
                
                if (PropertyChanged == null)
                    return;
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(@"SubmissionFacilityVM"));
            }
        }

        public DPoWFacilityViewModel SubmissionFacilityVM { get; private set; }

        private Facility _validationFacility;

        internal Facility ValidationFacility
        {
            get { return _validationFacility; }
            set
            {
                _validationFacility = value;
                ValidationFacilityVM = new DPoWFacilityViewModel(_validationFacility);

                if (PropertyChanged == null)
                    return;
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(@"ValidationFacilityVM"));
            }
        }

        public DPoWFacilityViewModel ValidationFacilityVM { get; private set; }

        public string SubmissionFileSource
        {
            get { return SubmissionFileInfo.File; }
            set
            {
                SubmissionFileInfo.File = value;
                Validate.ChangesHappened();
            }
        }

        internal SourceFile RequirementFileInfo = new SourceFile();
        internal SourceFile SubmissionFileInfo = new SourceFile();
        
        public ValidationViewModel()
        {
            IsWorking = false;
            SelectRequirement = new SelectFileCommand(RequirementFileInfo, this);
            SelectSubmission = new SelectFileCommand(SubmissionFileInfo, this) {IncludeIfc = true};

            Validate = new ValidateCommand(this);
        }

        public event PropertyChangedEventHandler PropertyChanged;

        internal void FilesUpdate()
        {
            if (PropertyChanged == null) 
                return;
            PropertyChanged.Invoke(this, new PropertyChangedEventArgs(@"RequirementFileSource"));
            PropertyChanged.Invoke(this, new PropertyChangedEventArgs(@"SubmissionFileSource"));
            Validate.ChangesHappened();
        }

        internal void ExecuteValidation()
        {
            IsWorking = true;
            PropertyChanged.Invoke(this, new PropertyChangedEventArgs(@"FilesCanChange"));
            SelectRequirement.ChangesHappened();
            SelectSubmission.ChangesHappened();
            
            LoadRequirementFile(RequirementFileSource);
            LoadSubmissionFile(SubmissionFileSource);
            

        }

        private void LoadRequirementFile(string cobieFilename)
        {
         
            
            if (string.IsNullOrEmpty(cobieFilename))
                return;
            if (!File.Exists(cobieFilename))
                return;

            switch (Path.GetExtension(cobieFilename.ToLowerInvariant()))
            {
                case ".json":
                    RequirementFacility = Facility.ReadJson(cobieFilename);
                    break;
                case ".xml":
                    RequirementFacility = Facility.ReadXml(cobieFilename);
                    break;
            }
        }

        private string _openedModelFileName;
        private string _temporaryXbimFileName;

        private BackgroundWorker _worker;

        private void OpenSubmissionCobiFile(object s, DoWorkEventArgs args)
        {
            var worker = s as BackgroundWorker;
            var cobieFilename = args.Argument as string;
            if (string.IsNullOrEmpty(cobieFilename))
                return;
            if (!File.Exists(cobieFilename))
                return;
            
            switch (Path.GetExtension(cobieFilename.ToLowerInvariant()))
            {
                case ".json": 
                    SubmissionFacility = Facility.ReadJson(cobieFilename);
                    break;
                case ".xml":
                    SubmissionFacility = Facility.ReadXml(cobieFilename);
                    break;
            }
            args.Result = SubmissionFacility;
        }

        private void OpenIfcFile(object s, DoWorkEventArgs args)
        {
            var worker = s as BackgroundWorker;
            var ifcFilename = args.Argument as string;

            var model = new XbimModel();
            try
            {
                _temporaryXbimFileName = Path.GetTempFileName();
                _openedModelFileName = ifcFilename;

                if (worker != null)
                {
                    model.CreateFrom(ifcFilename, _temporaryXbimFileName, worker.ReportProgress, true);
                    var context = new Xbim3DModelContext(model);//upgrade to new geometry represenation, uses the default 3D model
                    context.CreateContext(geomStorageType: XbimGeometryType.PolyhedronBinary, progDelegate: worker.ReportProgress);

                    if (worker.CancellationPending) //if a cancellation has been requested then don't open the resulting file
                    {
                        try
                        {
                            model.Close();
                            if (File.Exists(_temporaryXbimFileName))
                                File.Delete(_temporaryXbimFileName); 
                            _temporaryXbimFileName = null;
                            _openedModelFileName = null;
                        }
                        // ReSharper disable once EmptyGeneralCatchClause
                        catch
                        {

                        }
                        return;
                    }
                }
                args.Result = model;
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Error reading " + ifcFilename);
                var indent = "\t";
                while (ex != null)
                {
                    sb.AppendLine(indent + ex.Message);
                    ex = ex.InnerException;
                    indent += "\t";
                }
                args.Result = new Exception(sb.ToString());
            }
        }

        private void OpenXbimFile(object s, DoWorkEventArgs args)
        {
            var worker = s as BackgroundWorker;
            var fileName = args.Argument as string;
            var model = new XbimModel();
            try
            {
                const XbimDBAccess dbAccessMode = XbimDBAccess.Read;
                if (worker != null)
                {
                    model.Open(fileName, dbAccessMode, worker.ReportProgress); //load entities into the model

                    if (model.IsFederation)
                    {
                        // needs to open the federation in rw mode
                        model.Close();
                        model.Open(fileName, XbimDBAccess.ReadWrite, worker.ReportProgress);
                        // federations need to be opened in read/write for the editor to work

                        // sets a convenient integer to all children for model identification
                        // this is used by the federated model selection mechanisms.
                        var i = 0;
                        foreach (var item in model.AllModels)
                        {
                            item.Tag = i++;
                        }
                    }
                }
                args.Result = model;
            }
            catch (Exception ex)
            {
                var sb = new StringBuilder();
                sb.AppendLine("Error reading " + fileName);
                var indent = "\t";
                while (ex != null)
                {
                    sb.AppendLine(indent + ex.Message);
                    ex = ex.InnerException;
                    indent += "\t";
                }
                args.Result = new Exception(sb.ToString());
            }
        }

        private string _activityStatus;
        public string ActivityStatus
        {
            get { return _activityStatus; }

            set
            {
                _activityStatus = value;
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(@"ActivityStatus"));
            }
        }

        private int _activityProgress;
        public int ActivityProgress
        {
            get { return _activityProgress; }
            set
            {
                _activityProgress = value;
                PropertyChanged.Invoke(this, new PropertyChangedEventArgs(@"ActivityProgress"));
            }
        }
        public string ActivityDescription { get; set; }

        


        private XbimModel _model;

        private void CreateWorker()
        {
            _worker = new BackgroundWorker
            {
                WorkerReportsProgress = true, 
                WorkerSupportsCancellation = true
            };
            _worker.ProgressChanged += delegate(object s, ProgressChangedEventArgs args)
            {
                ActivityProgress = args.ProgressPercentage;
                ActivityStatus = (string) args.UserState;
                Debug.WriteLine("{0}% {1}", args.ProgressPercentage, (string) args.UserState);
            };

            _worker.RunWorkerCompleted += delegate(object s, RunWorkerCompletedEventArgs args)
            {
                if (args.Result is XbimModel) //all ok
                {
                    _model = args.Result as XbimModel;
                    ActivityProgress = 0;
                    // prepare the facility
                    var helper = new CoBieLiteUkHelper(_model, "NBS Code");
                    SubmissionFacility = helper.GetFacilities().FirstOrDefault();
                    ValidateLoadedFacilities();
                }
                else if (args.Result is Facility) //all ok; this is the model facility
                {
                    ValidateLoadedFacilities();
                }
                else //we have a problem
                {
                    var errMsg = args.Result as String;
                    if (!string.IsNullOrEmpty(errMsg))
                    {
                        ActivityStatus = "Error Opening File";
                        // MessageBox.Show(this, errMsg, "Error Opening File", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.None, MessageBoxOptions.None);
                    }
                    if (args.Result is Exception)
                    {
                        var sb = new StringBuilder();
                        var ex = args.Result as Exception;
                        var indent = "";
                        while (ex != null)
                        {
                            sb.AppendFormat("{0}{1}\n", indent, ex.Message);
                            ex = ex.InnerException;
                            indent += "\t";
                        }
                        // todo: restore
                        // MessageBox.Show(this, sb.ToString(), "Error Opening Ifc File", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.None, MessageBoxOptions.None);
                    }
                    ActivityProgress = 0;
                    // todo: restore
                    // StatusMsg.Text = "Error/Ready";
                }
                // todo: restore
                // FireLoadingComplete(s, args);
            };
        }

        private void ValidateLoadedFacilities()
        {
            var f = new cobieUKValidation.FacilityValidator();
            ValidationFacility = f.Validate(RequirementFacility, SubmissionFacility);
        }

        private void CloseAndDeleteTemporaryFiles()
        {
            try
            {
                if (_worker != null && _worker.IsBusy)
                    _worker.CancelAsync(); //tell it to stop
                
                _openedModelFileName = null;
                if (_model == null) 
                    return;
                _model.Dispose();
                _model = null;
            }
            finally
            {
                if (!(_worker != null && _worker.IsBusy && _worker.CancellationPending)) //it is still busy but has been cancelled 
                {
                    if (!string.IsNullOrWhiteSpace(_temporaryXbimFileName) && File.Exists(_temporaryXbimFileName))
                        File.Delete(_temporaryXbimFileName);
                    _temporaryXbimFileName = null;
                } //else do nothing it will be cleared up in the worker thread
            }
        }

        public void LoadSubmissionFile(string modelFileName)
        {
            var fInfo = new FileInfo(modelFileName);
            if (!fInfo.Exists) // file does not exist; do nothing
                return;
            
            // there's no going back; if it fails after this point the current file should be closed anyway
            CloseAndDeleteTemporaryFiles();
            _openedModelFileName = modelFileName.ToLower();
            
            CreateWorker();

            var ext = fInfo.Extension.ToLower();
            switch (ext)
            {
                case ".json": 
                case ".xml": 
                    _worker.DoWork += OpenSubmissionCobiFile;
                    _worker.RunWorkerAsync(modelFileName);
                    break;
                case ".ifc": //it is an Ifc File
                case ".ifcxml": //it is an IfcXml File
                case ".ifczip": //it is a xip file containing xbim or ifc File
                case ".zip": //it is a xip file containing xbim or ifc File
                    _worker.DoWork += OpenIfcFile;
                    _worker.RunWorkerAsync(modelFileName);
                    break;
                case ".xbimf":
                case ".xbim": //it is an xbim File, just open it in the main thread
                    _worker.DoWork += OpenXbimFile;
                    _worker.RunWorkerAsync(modelFileName);
                    break;
            }
        }
    }
}