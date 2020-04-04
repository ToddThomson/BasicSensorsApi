using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Gms.Auth.Api.SignIn;
using Android.Gms.Fitness;
using Android.Gms.Fitness.Data;
using Android.Gms.Fitness.Request;
using Android.Graphics;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Views;
using AndroidX.AppCompat.App;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using CommonSampleLibrary;
using Google.Android.Material.Snackbar;
using Java.Util.Concurrent;
using System;
using System.Threading.Tasks;

namespace BasicSensorsApi
{
    [Activity( Label = "@string/app_name", Theme = "@style/AppTheme", MainLauncher = true )]
    public class MainActivity : AppCompatActivity
    {
        public const string TAG = "BasicSensorsApi";

        IOnDataPointListener _dataPointListener = null;

        private FitnessOptions _fitnessOptions => FitnessOptions.InvokeBuilder()
            .AddDataType( DataType.TypeLocationSample )
            .Build();

        private bool IsRunningQOrLater => Build.VERSION.SdkInt >= Build.VERSION_CODES.Q;

        private bool IsOAuthPermissionsApproved => GoogleSignIn.HasPermissions( GoogleAccount, _fitnessOptions );

        /// <summary>
        /// Gets a Google account for use in creating the Fitness client. This is achieved by either
        /// using the last signed-in account, or if necessary, prompting the user to sign in.
        /// GetAccountForExtension is recommended over `GetLastSignedInAccount` as the latter can
        /// return `null` if there has been no sign in before.
        /// </summary>
        private GoogleSignInAccount GoogleAccount => GoogleSignIn.GetAccountForExtension( this, _fitnessOptions );

        protected override void OnCreate( Bundle savedInstanceState )
        {
            base.OnCreate( savedInstanceState );

            // Set our view from the "main" layout resource
            SetContentView( Resource.Layout.activity_main );

            InitializeLogging();

            // When permissions are revoked the app is restarted so onCreate is sufficient to check for
            // permissions core to the Activity's functionality.
            CheckPermissionsAndRun( FitActionRequestCode.FindDataSources );
        }

        /// <summary>
        /// Handles the callback from the OAuth sign in flow, executing the post sign in function.
        /// </summary>
        /// <param name="requestCode"></param>
        /// <param name="resultCode"></param>
        /// <param name="data"></param>
        protected override void OnActivityResult( int requestCode, [GeneratedEnum] Result resultCode, Intent data )
        {
            base.OnActivityResult( requestCode, resultCode, data );

            if ( resultCode == Result.Ok )
            {
                PerformActionForRequestCode( (FitActionRequestCode)requestCode );
            }
            else
            {
                OAuthErrorMsg( requestCode, (int)resultCode );
            }
        }

        public override bool OnCreateOptionsMenu( IMenu menu )
        {
            MenuInflater.Inflate( Resource.Menu.main, menu );

            return true;
        }

        public override bool OnOptionsItemSelected( IMenuItem item )
        {
            int id = item.ItemId;

            if ( id == Resource.Id.action_unregister_listener )
            {
                UnregisterFitnessDataListener();

                return true;
            }

            return base.OnOptionsItemSelected( item );
        }

        private void CheckPermissionsAndRun( FitActionRequestCode fitActionRequestCode )
        {
            if ( PermissionApproved() )
            {
                FitSignIn( fitActionRequestCode );
            }
            else
            {
                RequestRuntimePermissions( fitActionRequestCode );
            }
        }

        private bool PermissionApproved()
        {
            var approved = false;

            if ( IsRunningQOrLater )
            {
                var permission = ActivityCompat.CheckSelfPermission( this, Manifest.Permission.AccessFineLocation );

                approved = (permission == PermissionChecker.PermissionGranted);
            }
            else
            {
                approved = true;
            }

            return approved;
        }

        private void RequestRuntimePermissions( FitActionRequestCode requestCode )
        {
            var shouldProvideRationale = ActivityCompat.ShouldShowRequestPermissionRationale(
                this, Manifest.Permission.AccessFineLocation );

            // Provide an additional rationale to the user. This would happen if the user denied the
            // request previously, but didn't check the "Don't ask again" checkbox.
            if ( shouldProvideRationale )
            {
                Log.Info( TAG, "Displaying permission rationale to provide additional context." );

                Snackbar.Make(
                    FindViewById( Resource.Id.main_activity_view ),
                    Resource.String.permission_rationale,
                    Snackbar.LengthIndefinite )
                    .SetAction( Resource.String.ok, ( obj ) =>
                    {
                        // Request permission
                        ActivityCompat.RequestPermissions(
                            this,
                            new string[] { Manifest.Permission.AccessFineLocation },
                            (int)requestCode );
                    } )
                    .Show();
            }
            else
            {
                Log.Info( TAG, "Requesting permission(s)..." );

                // Request permission. It's possible this can be auto answered if device policy
                // sets the permission in a given state or the user denied the permission
                // previously and checked "Never ask again".
                ActivityCompat.RequestPermissions( this,
                        new string[] { Manifest.Permission.AccessFineLocation },
                        (int)requestCode );
            }
        }

        public override void OnRequestPermissionsResult( int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults )
        {
            if ( requestCode == (int)FitActionRequestCode.FindDataSources )
            {
                if ( grantResults.Length <= 0 )
                {
                    // If user interaction was interrupted, the permission request
                    // is cancelled and you receive empty arrays
                    Log.Info( TAG, "User interaction was cancelled." );
                }
                else
                {
                    if ( grantResults[ 0 ] == Permission.Granted )
                    {
                        // Permission was granted.
                        FitSignIn( FitActionRequestCode.FindDataSources );
                    }
                    else
                    {
                        // Permission denied.

                        // In this Activity we've chosen to notify the user that they
                        // have rejected a core permission for the app since it makes the Activity useless.
                        // We're communicating this message in a Snackbar since this is a sample app, but
                        // core permissions would typically be best requested during a welcome-screen flow.

                        // Additionally, it is important to remember that a permission might have been
                        // rejected without asking the user for permission (device policy or "Never ask
                        // again" prompts). Therefore, a user interface affordance is typically implemented
                        // when permissions are denied. Otherwise, your app could appear unresponsive to
                        // touches or interactions which have required permissions.

                        Snackbar.Make(
                            FindViewById( Resource.Id.main_activity_view ),
                            Resource.String.permission_denied_explanation,
                            Snackbar.LengthIndefinite )
                            .SetAction( Resource.String.settings, ( obj ) =>
                            {
                                // Build intent that displays the App settings screen.
                                var intent = new Intent();

                                intent.SetAction( Android.Provider.Settings.ActionApplicationDetailsSettings );
                                var uri = Android.Net.Uri.FromParts( "package", Application.Context.PackageName, null );
                                intent.SetData( uri );
                                intent.SetFlags( ActivityFlags.NewTask );

                                StartActivity( intent );
                            } )
                            .Show();
                    }
                }
            }
        }

        /// <summary>
        /// Checks that the user is signed in, and if so, executes the specified function. If the user is
        /// not signed in, initiates the sign in flow, specifying the post-sign in function to execute.
        /// </summary>
        /// <param name="requestCode">The request code corresponding to the action to perform after sign in.</param>
        private void FitSignIn( FitActionRequestCode requestCode )
        {
            if ( IsOAuthPermissionsApproved )
            {
                PerformActionForRequestCode( requestCode );
            }
            else
            {
                GoogleSignIn.RequestPermissions(
                        this,
                        (int)requestCode,
                        GoogleAccount,
                        _fitnessOptions );
            }
        }

        /// <summary>
        /// Runs the desired method, based on the specified request code. The request code is typically
        /// passed to the Fit sign-in flow, and returned with the success callback. This allows the
        /// caller to specify which method, post-sign-in, should be called.
        /// </summary>
        /// <param name="requestCode">The code corresponding to the action to perform.</param>
        private void PerformActionForRequestCode( FitActionRequestCode requestCode )
        {
            switch ( requestCode )
            {
                case FitActionRequestCode.FindDataSources:
#if USESENSORSCLIENT
                    FindFitnessDataSources();
#endif

#if USESENSORSCLIENT_ASYNC
                    FindFitnessDataSourcesAsync();
#endif

#if USESENSORSAPI
                    FindFitnessDataSourcesWithSensorsApi();
#endif
                    break;
            }
        }

        private void OAuthErrorMsg( int requestCode, int resultCode )
        {
            string message = $@"There was an error signing into Fit. Check the troubleshooting section of the README for potential issues.
            Request code was: {requestCode}
            Result code was: {resultCode}";

            Log.Error( TAG, message );
        }

        private void InitializeLogging()
        {
            // Wraps Android's native log framework.
            var logWrapper = new LogWrapper();

            // Using Log, front-end to the logging chain, emulates android.util.log method signatures.
            Log.LogNode = logWrapper;

            // Filter strips out everything except the message text.
            var msgFilter = new MessageOnlyLogFilter();
            logWrapper.NextNode = msgFilter;

            // On screen logging via a customized TextView.
            var logView = FindViewById<LogView>( Resource.Id.sample_logview );
            logView.SetTextAppearance( this, Resource.Style.Log );
            logView.SetBackgroundColor( Color.White );
            msgFilter.NextNode = logView;

            Log.Info( TAG, "Ready" );
        }

        /// <summary>
        /// Finds Fit DataSources using the SensorsClient API.
        /// </summary>
        private async Task FindFitnessDataSources()
        {
            DataSourcesRequest request = new DataSourcesRequest.Builder()
                .SetDataTypes( DataType.TypeLocationSample )
                .SetDataSourceTypes( DataSource.TypeRaw )
                .Build();

            var client = FitnessClass.GetSensorsClient( this, GoogleAccount );

            var dataSourcesSuccessListener = new DataSourcesSuccessListener
            {
                OnSuccessImpl = async ( dataSources ) =>
                {
                    foreach ( DataSource dataSource in dataSources )
                    {
                        Log.Info( TAG, "Data source found: " + dataSource );
                        Log.Info( TAG, "Data Source type: " + dataSource.DataType.Name );

                        // NOTE: We used DataType.Name here as the test for equality between DataType was false.
                        // The reason for this should be determined.

                        // Let's register a listener to receive Activity data!
                        if ( (dataSource.DataType.Name == DataType.TypeLocationSample.Name) && (_dataPointListener == null) )
                        {
                            Log.Info( TAG, "Data source for LOCATION_SAMPLE found!  Registering." );
                            await RegisterFitnessDataListener( dataSource, DataType.TypeLocationSample );
                        }
                    }
                }
            };

            client.FindDataSources( request ).AddOnSuccessListener( dataSourcesSuccessListener );
        }

        /// <summary>
        /// Finds Fit DataSources using SensorsClient API.
        /// Fails with exception at FindDataSourcesAsync()
        /// </summary>
        private async Task FindFitnessDataSourcesAsync()
        {
            DataSourcesRequest request = new DataSourcesRequest.Builder()
                .SetDataTypes( DataType.TypeLocationSample )
                .SetDataSourceTypes( DataSource.TypeRaw )
                .Build();

            var client = FitnessClass.GetSensorsClient( this, GoogleAccount );

            var dataSources = await client.FindDataSourcesAsync( request );

            foreach ( DataSource dataSource in dataSources )
            {
                Log.Info( TAG, "Data source found: " + dataSource );
                Log.Info( TAG, "Data Source type: " + dataSource.DataType.Name );

                // NOTE: We used DataType.Name here as the test for equality between DataType was false.
                // The reason for this should be determined.

                // Let's register a listener to receive Activity data!
                if ( (dataSource.DataType.Name == DataType.TypeLocationSample.Name) && (_dataPointListener == null) )
                {
                    Log.Info( TAG, "Data source for LOCATION_SAMPLE found!  Registering." );
                    await RegisterFitnessDataListener( dataSource, DataType.TypeLocationSample );
                }
            }
        }

        /// <summary>
        /// Finds Fit DataSources using the deprecated Fitness.SensorsApi.
        /// </summary>
        private async Task FindFitnessDataSourcesWithSensorsApi()
        {
            DataSourcesRequest request = new DataSourcesRequest.Builder()
                .SetDataTypes( DataType.TypeLocationSample )
                .SetDataSourceTypes( DataSource.TypeRaw )
                .Build();

            var client = FitnessClass.GetSensorsClient( this, GoogleAccount );

            var dataSourcesResult = await FitnessClass.SensorsApi.FindDataSourcesAsync( client.AsGoogleApiClient(), request );

            Log.Info( TAG, "Result: " + dataSourcesResult.Status );

            foreach ( DataSource dataSource in dataSourcesResult.DataSources )
            {
                Log.Info( TAG, "Data source found: " + dataSource );
                Log.Info( TAG, "Data Source type: " + dataSource.DataType.Name );

                // NOTE: We used DataType.Name here as the test for equality between DataType was false.
                // The reason for this should be determined.

                // Let's register a listener to receive Activity data!
                if ( (dataSource.DataType.Name == DataType.TypeLocationSample.Name) && (_dataPointListener == null) )
                {
                    Log.Info( TAG, "Data source for LOCATION_SAMPLE found!  Registering." );
                    await RegisterFitnessDataListener( dataSource, DataType.TypeLocationSample );
                }
            }
        }

        private async Task RegisterFitnessDataListener( DataSource dataSource, DataType dataType )
        {
            var request = new SensorRequest.Builder()
                .SetDataSource( dataSource ) // Optional but recommended for custom data sets.
                .SetDataType( dataType ) // Can't be omitted.
                .SetSamplingRate( 10, TimeUnit.Seconds )
                .Build();

            _dataPointListener = new OnDataPointListener();

            var client = FitnessClass.GetSensorsClient( this, GoogleAccount );

            await client.AddAsync( request, _dataPointListener );

            Log.Info( TAG, "Listener registered." );

            //if ( status.IsSuccess )
            //{
            //    Log.Info( TAG, "Listener registered." );
            //}
            //else
            //{
            //    Log.Info( TAG, "Listener not registered." );
            //}
        }

        private async Task UnregisterFitnessDataListener()
        {
            if ( _dataPointListener == null )
            {
                return;
            }

            var client = FitnessClass.GetSensorsClient( this, GoogleAccount );

            var status = await client.RemoveAsync( _dataPointListener );

            if ( status.BooleanValue() )
            {
                Log.Info( TAG, "Listener was removed!" );

                _dataPointListener = null;
            }
            else
            {
                Log.Info( TAG, "Listener was not removed." );
            }
        }

        private class OnDataPointListener : Java.Lang.Object, IOnDataPointListener
        {
            public void OnDataPoint( DataPoint dataPoint )
            {
                foreach ( var field in dataPoint.DataType.Fields )
                {
                    Value val = dataPoint.GetValue( field );
                    Log.Info( TAG, "Detected DataPoint field: " + field.Name );
                    Log.Info( TAG, "Detected DataPoint value: " + val );
                }
            }
        }

        private class DataSourcesSuccessListener : Java.Lang.Object, Android.Gms.Tasks.IOnSuccessListener
        {
            public Action<JavaCollection<DataSource>> OnSuccessImpl { get; set; }

            public void OnSuccess( Java.Lang.Object result )
            {
                OnSuccessImpl( result.JavaCast<JavaCollection<DataSource>>() );
            }
        }

        private class DataSourcesFailureListener : Java.Lang.Object, Android.Gms.Tasks.IOnFailureListener
        {
            public void OnFailure( Java.Lang.Exception e )
            {
                Log.Error( TAG, $" [{DateTime.Now}] - [AndroidAppLinks Failure] - {e.Message}" );

                throw e;
            }
        }

        private enum FitActionRequestCode : int
        {
            FindDataSources = 1
        }
    }
}
