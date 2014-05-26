﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using RDotNet;
using RDotNet.NativeLibrary;

namespace Rclr
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate void Rf_error(string msg);

    public class RDotNetDataConverter : IDataConverter
    {
        private RDotNetDataConverter ()
        {
            SetupREngine ();
            // The Mono API already has some unhandled exception reporting. 
            // TODO Use the following if it works well for both CLRuntimes.
#if !MONO
            SetupExceptionHandling();
#endif
            ConvertVectors = true;
            ConvertValueTypes = true;

            converterFunctions = new Dictionary<Type, Func<object, SymbolicExpression>>();

            converterFunctions.Add(typeof(float), ConvertSingle);
            converterFunctions.Add(typeof(double), ConvertDouble);
            //converterFunctions.Add(typeof(bool), ConvertDouble);
            converterFunctions.Add(typeof(int), ConvertInt);
            converterFunctions.Add(typeof(string), ConvertString);
            converterFunctions.Add(typeof(DateTime), ConvertDateTime);
            converterFunctions.Add(typeof(TimeSpan), ConvertTimeSpan);

            converterFunctions.Add(typeof(float[]), ConvertArraySingle);
            converterFunctions.Add(typeof(double[]), ConvertArrayDouble);
            //converterFunctions.Add(typeof(bool[]), ConvertArrayDouble);
            converterFunctions.Add(typeof(int[]), ConvertArrayInt);
            converterFunctions.Add(typeof(string[]), ConvertArrayString);
            converterFunctions.Add(typeof(DateTime[]), ConvertArrayDateTime);
            converterFunctions.Add(typeof(TimeSpan[]), ConvertArrayTimeSpan);

            converterFunctions.Add(typeof(float[,]), ConvertMatrixSingle);
            converterFunctions.Add(typeof(double[,]), ConvertMatrixDouble);
            converterFunctions.Add(typeof(int[,]), ConvertMatrixInt);
            converterFunctions.Add(typeof(string[,]), ConvertMatrixString);

            converterFunctions.Add(typeof(float[][]), ConvertMatrixJaggedSingle);
            converterFunctions.Add(typeof(double[][]), ConvertMatrixJaggedDouble);
            converterFunctions.Add(typeof(int[][]), ConvertMatrixJaggedInt);
            converterFunctions.Add(typeof(string[][]), ConvertMatrixJaggedString);

            converterFunctions.Add(typeof(Dictionary<string, double>), ConvertDictionary<double>);
            converterFunctions.Add(typeof(Dictionary<string, float>), ConvertDictionary<float>);
            converterFunctions.Add(typeof(Dictionary<string, string>), ConvertDictionary<string>);
            converterFunctions.Add(typeof(Dictionary<string, int>), ConvertDictionary<int>);
            converterFunctions.Add(typeof(Dictionary<string, DateTime>), ConvertDictionary<DateTime>);

            converterFunctions.Add(typeof(Dictionary<string, double[]>), ConvertDictionary<double[]>);
            converterFunctions.Add(typeof(Dictionary<string, float[]>), ConvertDictionary<float[]>);
            converterFunctions.Add(typeof(Dictionary<string, string[]>), ConvertDictionary<string[]>);
            converterFunctions.Add(typeof(Dictionary<string, int[]>), ConvertDictionary<int[]>);
            converterFunctions.Add(typeof(Dictionary<string, DateTime[]>), ConvertDictionary<DateTime[]>);

        }

        private void SetupExceptionHandling()
        {
            AppDomain.CurrentDomain.UnhandledException += OnUnhandleException;
        }

        private void OnUnhandleException(object sender, UnhandledExceptionEventArgs e)
        {
            Exception ex = (Exception)e.ExceptionObject;
            Error(ClrFacade.FormatExceptionInnermost(ex));
        }

        public void Error(string msg)
        {
            throw new NotSupportedException();
            //engine.Error(msg);
        }

        /// <summary>
        /// Enable/disable the use of this data converter in the R-CLR interop data marshalling.
        /// </summary>
        public static void SetRDotNet(bool setit)
        {
            if (setit)
                ClrFacade.DataConverter = GetInstance();
            else
                ClrFacade.DataConverter = null;
        }

        /// <summary>
        /// Convert an object, if possible, using RDotNet capabilities
        /// </summary>
        /// <remarks>
        /// If a conversion to an RDotNet SymbolicExpression was possible, 
        /// this returns the IntPtr SafeHandle.DangerousGetHandle() to be passed to R.
        /// If the object is null or such that no known conversion is possible, the same object 
        /// as the input parameter is returned.
        /// </remarks>
        public object ConvertToR (object obj)
        {
            ClearSexpHandles();
            if (obj == null) 
                return null;
            var sexp = obj as SymbolicExpression;
            if (sexp != null)
                return ReturnHandle(sexp);

            sexp = TryConvertToSexp(obj);

            if (sexp == null)
                return obj;
            return ReturnHandle(sexp);
        }

        private void ClearSexpHandles()
        {
            handles.Clear();
        }

        private static object ReturnHandle(SymbolicExpression sexp)
        {
            AddSexpHandle(sexp);
            return sexp.DangerousGetHandle();
        }

        private static void AddSexpHandle(SymbolicExpression sexp)
        {
            handles.Add(sexp);
        }

        /// <summary>
        /// A list to reference to otherwise transient SEXP created by this class. 
        /// This is to prevent .NET and R to trigger GC before rClr function calls have returned to R.
        /// </summary>
        private static List<SymbolicExpression> handles = new List<SymbolicExpression>();

        public object ConvertFromR(IntPtr pointer, int sexptype)
        {
            throw new NotImplementedException();
            //return new DataFrame(engine, pointer);
        }

        public bool ConvertVectors { get; set; }
        public bool ConvertValueTypes { get; set; }

        private void SetupREngine()
        {
            engine = REngine.GetInstance(initialize: false);
            engine.Initialize(setupMainLoop: false);
        }

        private static RDotNetDataConverter singleton;

        private static RDotNetDataConverter GetInstance()
        {
            // Make sure this is set only once (RDotNet known limitation to one engine per session, effectively a singleton).
            if (singleton == null)
                singleton = new RDotNetDataConverter();
            return singleton;
        }

        private Dictionary<Type, Func<object,SymbolicExpression>> converterFunctions;

        private SymbolicExpression TryConvertToSexp(object obj)
        {
            SymbolicExpression sHandle = null;
            if (obj == null)
                throw new ArgumentNullException("object to convert to R must not be a null reference");
            var t = obj.GetType();
            var converter = TryGetConverter(t);
            sHandle = (converter == null ? null : converter.Invoke(obj));
            return sHandle;
        }

        private Func<object, SymbolicExpression> TryGetConverter(Type t)
        {
            Func<object, SymbolicExpression> converter;
            if (converterFunctions.TryGetValue(t, out converter))
                return converter;
            return null;
        }

        //private bool TryGetValueAssignableValue(Type t, out Func<object, SymbolicExpression> converter)
        //{
        //    var assignable = converterFunctions.Keys.Where(x => x.IsAssignableFrom(t)).FirstOrDefault();
        //    if(assignable!=null)
        //    {
        //        assignable.
        //    if(converterFunctions.TryGetValue(t, out converter)
        //}

        private SymbolicExpression ConvertToSexp(object obj)
        {
            if (obj == null) return null;
            var result = TryConvertToSexp(obj);
            if(result==null)
                throw new NotSupportedException(string.Format("Cannot yet expose type {0} as a SEXP", obj.GetType().FullName));
            return result;
        }

        private GenericVector ConvertDictionary<U>(object obj)
        {
            var dict = (IDictionary<string, U>)obj;
            if (!converterFunctions.ContainsKey(typeof(U[])))
                throw new NotSupportedException("Cannot convert a dictionary of type " + dict.GetType()); 
            var values = converterFunctions[typeof(U[])].Invoke(dict.Values.ToArray());
            SetAttribute(values, dict.Keys.ToArray());
            return values.AsList();
        }

        private SymbolicExpression ConvertAll(object[] objects)
        {
            var sexpArray = new SymbolicExpression[objects.Length];
            for (int i = 0; i < objects.Length; i++)
                sexpArray[i] = ConvertToSexp(objects[i]);
            return new GenericVector(engine, sexpArray);
        }

        private SymbolicExpression ConvertArrayDouble(object obj)
        {
            if (!ConvertVectors) return null;
            double[] array = (double[])obj;
            return engine.CreateNumericVector(array);
        }

        private SymbolicExpression ConvertArraySingle(object obj)
        {
            if (!ConvertVectors) return null;
            float[] array = (float[])obj;
            return ConvertArrayDouble(Array.ConvertAll(array, x => (double)x));
        }

        private SymbolicExpression ConvertArrayInt(object obj)
        {
            if (!ConvertVectors) return null;
            int[] array = (int[])obj;
            return engine.CreateIntegerVector(array);
        }

        private SymbolicExpression ConvertArrayString(object obj)
        {
            if (!ConvertVectors) return null;
            string[] array = (string[])obj;
            return engine.CreateCharacterVector(array);
        }

        private SymbolicExpression ConvertArrayDateTime(object obj)
        {
            if (!ConvertVectors) return null;
            if (!ConvertValueTypes) return null;
            DateTime[] array = (DateTime[])obj;
            var doubleArray = Array.ConvertAll(array, ClrFacade.GetRPosixCtDoubleRepresentation);
            var result = ConvertArrayDouble(doubleArray);
            SetClassAttribute(result, "POSIXct", "POSIXt");
            SetTzoneAttribute(result, "UTC");
            return result;
        }

        private SymbolicExpression ConvertArrayTimeSpan(object obj)
        {
            if (!ConvertVectors) return null;
            if (!ConvertValueTypes) return null;
            TimeSpan[] array = (TimeSpan[])obj;
            var doubleArray = Array.ConvertAll(array, (x => x.TotalSeconds));
            var result = ConvertArrayDouble(doubleArray);
            SetClassAttribute(result, "difftime"); // class(as.difftime(3.5, units='secs'))
            SetUnitsAttribute(result, "secs");  // unclass(as.difftime(3.5, units='secs'))
            return result;
        }

        private SymbolicExpression ConvertDouble(object obj)
        {
            if (!ConvertVectors) return null;
            double value = (double)obj;
            return engine.CreateNumeric(value);
        }

        private SymbolicExpression ConvertSingle(object obj)
        {
            if (!ConvertVectors) return null;
            float value = (float)obj;
            return ConvertArrayDouble((double)value);
        }

        private SymbolicExpression ConvertBool(object obj)
        {
            if (!ConvertVectors) return null;
            bool value = (bool)obj;
            return engine.CreateLogical(value);
        }

        private SymbolicExpression ConvertInt(object obj)
        {
            if (!ConvertVectors) return null;
            int value = (int)obj;
            return engine.CreateInteger(value);
        }

        private SymbolicExpression ConvertString(object obj)
        {
            if (!ConvertVectors) return null;
            string value = (string)obj;
            return engine.CreateCharacter(value);
        }

        private SymbolicExpression ConvertDateTime(object obj)
        {
            if (!ConvertVectors) return null;
            if (!ConvertValueTypes) return null;
            DateTime value = (DateTime)obj;
            var doubleValue = ClrFacade.GetRPosixCtDoubleRepresentation(value);
            var result = ConvertDouble(doubleValue);
            SetClassAttribute(result, "POSIXct", "POSIXt");
            SetTzoneAttribute(result, "UTC");
            return result;
        }

        private SymbolicExpression ConvertTimeSpan(object obj)
        {
            if (!ConvertVectors) return null;
            if (!ConvertValueTypes) return null;
            TimeSpan value = (TimeSpan)obj;
            var doubleValue = value.TotalSeconds;
            var result = ConvertDouble(doubleValue);
            SetClassAttribute(result, "difftime"); // class(as.difftime(3.5, units='secs'))
            SetUnitsAttribute(result, "secs");  // unclass(as.difftime(3.5, units='secs'))
            return result;
        }

        private SymbolicExpression ConvertToList(object[] array)
        {
            return ConvertAll(array);
        }

        private SymbolicExpression ConvertMatrixJaggedSingle(object obj)
        {
            float[][] array = (float[][])obj;
            if (array.IsRectangular())
                return ConvertMatrixDouble(array.ToDoubleRect());
            else
                return ConvertToList(array.ToDouble());
        }

        private SymbolicExpression ConvertMatrixJaggedDouble(object obj)
        {
            double[][] array = (double[][])obj;
            if (array.IsRectangular())
                return ConvertMatrixDouble(array.ToRect());
            else
                return ConvertToList(array);
        }

        private SymbolicExpression ConvertMatrixJaggedInt(object obj)
        {
            int[][] array = (int[][])obj;
            if (array.IsRectangular())
                return ConvertMatrixInt(array.ToRect());
            else
                return ConvertToList(array);
        }

        private SymbolicExpression ConvertMatrixJaggedString(object obj)
        {
            string[][] array = (string[][])obj;
            if (array.IsRectangular())
                return ConvertMatrixString(array.ToRect());
            else
                return ConvertToList(array);
        }

        private NumericMatrix ConvertMatrixSingle(object obj)
        {
            float[,] array = (float[,])obj;
            return ConvertMatrixDouble(array.ToDoubleRect());
        }

        private NumericMatrix ConvertMatrixDouble(object obj)
        {
            double[,] array = (double[,])obj;
            return engine.CreateNumericMatrix(array);
        }

        private IntegerMatrix ConvertMatrixInt(object obj)
        {
            int[,] array = (int[,])obj;
            return engine.CreateIntegerMatrix(array);
        }

        private CharacterMatrix ConvertMatrixString(object obj)
        {
            string[,] array = (string[,])obj;
            return engine.CreateCharacterMatrix(array);
        }

        private void SetTzoneAttribute(SymbolicExpression sexp, string tzoneId)
        {
            SetAttribute(sexp, new[] { tzoneId }, attributeName: "tzone");
        }

        private void SetUnitsAttribute(SymbolicExpression sexp, string units)
        {
            SetAttribute(sexp, new[] { units }, attributeName: "units");
        }

        private void SetClassAttribute(SymbolicExpression sexp, params string[] classes)
        {
            SetAttribute(sexp, classes, attributeName: "class");
        }

        private void SetAttribute(SymbolicExpression sexp, string[] attribValues, string attributeName = "names")
        {
            var names = new CharacterVector(engine, attribValues);
            sexp.SetAttribute(attributeName, names);
        }

        [Obsolete()]
        private static void CheckEnvironmentVariables ()
        {
            var rlibFilename = getRDllName ();
            var searchPaths = (Environment.GetEnvironmentVariable ("PATH") ?? "").Split (Path.PathSeparator);
//            if( !searchPaths.Contains("/usr/lib"))
//                searchPaths.ToList().Add()
            var pathsWithRdll = searchPaths.Where ((x => File.Exists (Path.Combine (x, rlibFilename))));
            bool rdllInPath = (pathsWithRdll.Count () > 0);
            if (!rdllInPath)
                throw new Exception (string.Format("'{0}' not found in any of the paths in environment variable PATH", rlibFilename));
            var rhome = (Environment.GetEnvironmentVariable ("R_HOME") ?? "");
            if (string.IsNullOrEmpty (rhome)) {
                // It is OK: the call to Initialize on the REngine will set up R_HOME.
                //throw new Exception("environment variable R_HOME is not set");
            }
        }

        private static string getRDllName ()
        {
            return NativeUtility.GetRDllFileName();
        }

        public REngine engine;

        public REngine GetEngine()
        {
            return engine;
        }

    }
}