using System;
using Android.App;
using Android.Content;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using Android.OS;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using Android.Util;
using Android.Provider;


namespace MusicDrucker
{
	[Activity (Label = "MusicDrucker", MainLauncher = true)]
	public class MainActivity : ListActivity
	{
		List<QueueTrack> tracks;
		System.Timers.Timer t;
		HomeScreenAdapter hsa;

		protected override void OnCreate (Bundle bundle)
		{
			RequestWindowFeature(WindowFeatures.NoTitle);

			base.OnCreate (bundle);	


			tracks = parseLpq ();

			ListView listView;

			t = new System.Timers.Timer();
			t.Interval = 1000;
			t.Elapsed += new System.Timers.ElapsedEventHandler(t_Elapsed);
			t.Start();

			SetContentView(Resource.Layout.Main);
			listView = FindViewById<ListView>(Android.Resource.Id.List);

			hsa = new HomeScreenAdapter(this, tracks);
			listView.Adapter = hsa;

			ImageButton btn_reload = FindViewById<ImageButton> (Resource.Id.imageButton1);
			btn_reload.Click += this.selectFile;

		}

		protected void t_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
		{
			t.Stop();
			this.RunOnUiThread(() =>{
				List<QueueTrack> new_t = parseLpq ();
				hsa.update_data(new_t);
				hsa.NotifyDataSetChanged();

			});
			t.Interval = 1000;
			t.Start ();
		}

		private void selectFile(object sender, EventArgs eventArgs)
		{
			Intent = new Intent();
			Intent.SetType("audio/*");
			Intent.SetAction(Intent.ActionGetContent);
			StartActivityForResult(Intent.CreateChooser(Intent, "Select File"), 3000);
		}

		private String getRealPathFromURI(Android.Net.Uri contentURI) {
			Android.Database.ICursor cursor = ContentResolver.Query (contentURI, null, null, null, null);
			if (cursor == null) // Source is Dropbox or other similar local file path
				return contentURI.Path;
			else {
				cursor.MoveToFirst ();
				int idx = cursor.GetColumnIndex (MediaStore.Audio.AudioColumns.Data);
				return cursor.GetString (idx);
			}
		}

		protected override void OnActivityResult(int requestCode, Result resultCode, Intent data)
		{
			if ((requestCode == 3000) && (resultCode == Result.Ok) && (data != null))
			{
				t.Stop ();
				Android.Net.Uri uri = data.Data;
				string path = getRealPathFromURI(uri);
				Printer prin = new Printer ("172.30.200.17", "lp", "android");
				prin.Restart ();
				prin.LPR (path);
				t.Interval = 1000;
				t.Start ();
			}
		}

		private List<QueueTrack> parseLpq()
		{
					// [XXX] active NEO\Stefan 268   ...                                  5705728 bytes
			Regex multiLine = new Regex(@"(?<user>[a-zA-Z-\\]+):\s+(?<status>[a-z0-9]+)\s+\[job\s+(?<jobid>[0-9]+)(?<details>[a-z0-9\.\s-]+)\]\s+(?<title>[\x20-\x7e]+)\.?.*\s+(?<size>\d+) bytes", RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.Multiline);

			List<QueueTrack> tracks = new List<QueueTrack> ();

			Printer prin = new Printer ("172.30.200.17", "lp", "android");

			if (!prin.ErrorMsg.Equals(""))
			{
				// need a popup here !
				// notifyIcon1.ShowBalloonTip(2000, "Error while spooling", printer1.ErrorMsg, ToolTipIcon.Error);
				return tracks;
			}
			prin.Restart ();
			String lines = prin.LPQ(false).Trim().Replace("\0", string.Empty);
			MatchCollection matches = multiLine.Matches(lines);

			foreach (Match m in matches)
			{
				String user = m.Result("${user}").Trim();
				String status = m.Result("${status}").Trim();
				String jobid = m.Result("${jobid}").Trim();
				String details = m.Result("${details}").Trim();
				String title = m.Result("${title}").Trim();
				String size = m.Result("${size}").Trim();

				int bytes = 0;
				Int32.TryParse(size, out bytes);
				//Log.Info ("OOOOOOOO", String.Format("{0,-8} {1,-15} {2,-6}MB {3}", status, user, ConvertIntToMegabytes(bytes).ToString("0.00"), title));
				QueueTrack nqt = new QueueTrack (user, status, details, title, ConvertIntToMegabytes (bytes).ToString ("0.00"));
				if (status == "active") {
					nqt.resourceId = Resource.Drawable.play;
				} else {
					nqt.resourceId = Resource.Drawable.wait;
				}

				tracks.Add(nqt);
			}

			return tracks;

		}

		static double ConvertIntToMegabytes(int bytes)
		{
			return (bytes / 1024f) / 1024f;
		}

			
	}

	public class QueueTrack
	{
		public String user { get; private set; }
		public String status { get; private set; }
		public String details { get; private set; }
		public String title { get; private set; }
		public String size { get; private set; }
		public int resourceId { get; set; }

		public QueueTrack(String user, String status, String details, String title, String size)
		{
			this.user = user;
			this.status = status;
			this.details = details;
			this.title = title;
			this.size = size;
		}
	}

	public class HomeScreenAdapter : BaseAdapter<QueueTrack> {

		List<QueueTrack> tracks;
		Activity context;

		public HomeScreenAdapter(Activity context, List<QueueTrack> tracks) : base() {
			this.context = context;
			this.tracks = tracks;
		}
		public override long GetItemId(int position)
		{
			return position;
		}
		public override QueueTrack this[int position] {  
			get { return tracks[position]; }
		}
		public override int Count {
			get { return tracks.Count; }
		}
		public override View GetView(int position, View convertView, ViewGroup parent)
		{
			var item = tracks[position];
			View view = convertView; // re-use an existing view, if one is available
			if (view == null) // otherwise create a new one
				view = context.LayoutInflater.Inflate(Resource.Layout.ListItemView, null);
			view.FindViewById<TextView>(Resource.Id.Text1).Text = item.title;
			view.FindViewById<TextView>(Resource.Id.Text2).Text = item.user;
			view.FindViewById<TextView>(Resource.Id.Text3).Text = item.size + " MB";
			view.FindViewById<ImageView>(Resource.Id.Image).SetImageResource(item.resourceId);

			return view;
		}
		public void update_data(List<QueueTrack> new_tracks) {
			this.tracks = new_tracks;
		}
	}
}


