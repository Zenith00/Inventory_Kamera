﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Accord.Imaging;
using Accord.Imaging.Filters;

namespace InventoryKamera
{
	public static class WeaponScraper
	{
		public static bool StopScanning { get; set; }

		public static void ScanWeapons(int count = 0)
		{
			// Determine maximum number of weapons to scan
			int weaponCount = count == 0 ? ScanWeaponCount(): count;
			var (rectangles, cols, rows) = GetPageOfItems();
			int fullPage = cols * rows;
			int totalRows = (int)Math.Ceiling(weaponCount / (decimal)cols);
			int cardsQueued = 0;
			int rowsQueued = 0;
			int offset = 0;
			UserInterface.SetWeapon_Max(weaponCount);

			// Enhancement ores being in the same inventory as weapons
			// can throw off scrolling through the inventory in certain circumstances
			// This fix is fine for now since most people don't scan below 3 stars
			// TODO: Rewrite this method to allow for scanning enhancement ore quantities
			weaponCount += 3;

			// Determine Delay if delay has not been found before
			// Scraper.FindDelay(rectangles);

			StopScanning = false;

			// Go through weapon list
			while (cardsQueued < weaponCount)
			{
				int cardsRemaining =  weaponCount - cardsQueued ;
				// Go through each "page" of items and queue. In the event that not a full page of
				// items are scrolled to, offset the index of rectangle to start clicking from
				for (int i = cardsRemaining < fullPage ? ( rows - ( totalRows - rowsQueued ) ) * cols : 0; i < rectangles.Count; i++)
				{
					Rectangle item = rectangles[i];
					Navigation.SetCursorPos(Navigation.GetPosition().Left + item.Center().X, Navigation.GetPosition().Top + item.Center().Y + offset);
					Navigation.Click();
					Navigation.SystemRandomWait(Navigation.Speed.SelectNextInventoryItem);

					// Queue card for scanning
					QueueScan(cardsQueued);
					cardsQueued++;
					if (cardsQueued >= weaponCount || StopScanning)
					{
						return;
					}
				}

				rowsQueued += rows;

				// Page done, now scroll
				// If the number of remaining scans is shorter than a full page then
				// only scroll a few rows
				if (totalRows - rowsQueued <= rows)
				{
					if (Navigation.GetAspectRatio() == new Size(8, 5))
					{
						offset = 35; // Lazy fix
					}
					for (int i = 0; i < 10 * ( totalRows - rowsQueued ) - 1; i++)
					{
						Navigation.sim.Mouse.VerticalScroll(-1);
					}
					Navigation.SystemRandomWait(Navigation.Speed.Fast);
				}
				else
				{
					// Scroll back one to keep it from getting too crazy
					if (rowsQueued % 15 == 0)
					{
						Navigation.sim.Mouse.VerticalScroll(1);
					}
					for (int i = 0; i < 10 * rows - 1; i++)
					{
						Navigation.sim.Mouse.VerticalScroll(-1);
					}
					Navigation.SystemRandomWait(Navigation.Speed.Fast);
				}
			}
		}

		public static int ScanWeaponCount()
		{
			//Find weapon count
			Rectangle region = new Rectangle(
				x: (int)( 1030 / 1280.0 * Navigation.GetWidth() ),
				y: (int)( 20 / 720.0 * Navigation.GetHeight() ),
				width: (int)( 175 / 1280.0 * Navigation.GetWidth() ),
				height: (int)( 25 / 720.0 * Navigation.GetHeight() ));

			using (Bitmap countBitmap = Navigation.CaptureRegion(region))
			{
				UserInterface.SetNavigation_Image(countBitmap);

				Bitmap n = Scraper.ConvertToGrayscale(countBitmap);
				Scraper.SetContrast(60.0, ref n);
				Scraper.SetInvert(ref n);

				string text = Scraper.AnalyzeText(n).Trim();
				n.Dispose();

				// Remove any non-numeric and '/' characters
				text = Regex.Replace(text, @"[^0-9/]", string.Empty);

				if (string.IsNullOrWhiteSpace(text))
				{
					countBitmap.Save($"./logging/weapons/WeaponCount.png");
					Navigation.CaptureWindow().Save($"./logging/weapons/WeaponWindow_{Navigation.GetWidth()}x{Navigation.GetHeight()}.png");
					throw new FormatException("Unable to locate weapon count.");
				}

				int count;

				// Check for slash
				if (Regex.IsMatch(text, "/"))
				{
					count = int.Parse(text.Split('/')[0]);
					Debug.WriteLine($"Parsed {count} for weapon count");
				}
				else if (Regex.Matches(text, "2000").Count == 1) // Remove the inventory limit from number
				{
					text = text.Replace("2000", string.Empty);
					count = int.Parse(text);
					Debug.WriteLine($"Parsed {count} for weapon count");
				}
				else // Extreme worst case
				{
					count = 2000;
					Debug.WriteLine("Defaulted to 2000 for weapon count");
				}

				return count;
			}
		}

		private static (List<Rectangle> rectangles, int cols, int rows) GetPageOfItems()
		{
			// Size of an item card is the same in 16:10 and 16:9. Also accounts for character icon and resolution size.
			var card = new RECT(
				Left: 0,
				Top: 0,
				Right: (int)(85 / 1280.0 * Navigation.GetWidth()),
				Bottom: (int)(105 / 720.0 * Navigation.GetHeight()));

			// Filter for relative size of items in inventory, give or take a few pixels
			using (BlobCounter blobCounter = new BlobCounter
			{
				FilterBlobs = true,
				MinHeight = card.Height - 10,
				MaxHeight = card.Height + 10,
				MinWidth = card.Width - 10,
				MaxWidth = card.Width + 10,
			})
			{
				// Screenshot of inventory
				Bitmap screenshot = Navigation.CaptureWindow();
				Bitmap output = new Bitmap(screenshot); // Copy used to overlay onto in testing

				// Image pre-processing
				ContrastCorrection contrast = new ContrastCorrection(85);
				Grayscale grayscale = new Grayscale(0.2125, 0.7154, 0.0721);
				Edges edges = new Edges();
				Threshold threshold = new Threshold(15);
				FillHoles holes = new FillHoles
				{
					CoupledSizeFiltering = true,
					MaxHoleWidth = card.Width + 10,
					MaxHoleHeight = card.Height + 10
				};
				SobelEdgeDetector sobel = new SobelEdgeDetector();

				screenshot = contrast.Apply(screenshot);
				screenshot = edges.Apply(screenshot); // Quick way to find ~75% of edges
				screenshot = grayscale.Apply(screenshot);
				screenshot = threshold.Apply(screenshot); // Convert to black and white only based on pixel intensity

				screenshot = sobel.Apply(screenshot); // Find some more edges
				screenshot = holes.Apply(screenshot); // Fill shapes
				screenshot = sobel.Apply(screenshot); // Find edges of those shapes. A second pass removes edges within item card
													  //Navigation.DisplayBitmap(screenshot);

				blobCounter.ProcessImage(screenshot);
				// Note: Processing won't always detect all item rectangles on screen. Since the
				// background isn't a solid color it's a bit trickier to filter out.

				if (blobCounter.ObjectsCount < 7)
				{
					output.Save("./logging/weapons/WeaponInventory.png");

					screenshot.Dispose();
					output.Dispose();
					throw new Exception("Insufficient items found in weapon inventory");
				}

				// Don't save overlapping blobs
				List<Rectangle> rectangles = new List<Rectangle>();
				List<Rectangle> blobRects = blobCounter.GetObjectsRectangles().ToList();

				int sWidth = blobRects[0].Width;
				int sHeight = blobRects[0].Height;
				foreach (var rect in blobRects)
				{
					bool add = true;
					foreach (var item in rectangles)
					{
						Rectangle r1 = rect;
						Rectangle r2 = item;
						Rectangle intersect = Rectangle.Intersect(r1, r2);
						if (intersect.Width > r1.Width * .2)
						{
							add = false;
							break;
						}
					}
					if (add)
					{
						sWidth = Math.Min(sWidth, rect.Width);
						sHeight = Math.Min(sHeight, rect.Height);
						rectangles.Add(rect);
					}
				}

				// Items originally detected
				// new RectanglesMarker(rectangles, Color.Red).ApplyInPlace(output);

				// Determine X and Y coordinates for columns and rows, respectively
				var colCoords = new List<int>();
				var rowCoords = new List<int>();

				foreach (var item in rectangles)
				{
					bool addX = true;
					bool addY = true;
					foreach (var x in colCoords)
					{
						var xC = item.Center().X;
						if (x - 10 <= xC && xC <= x + 10)
						{
							addX = false;
							break;
						}
					}
					foreach (var y in rowCoords)
					{
						var yC = item.Center().Y;
						if (y - 10 <= yC && yC <= y + 10)
						{
							addY = false;
							break;
						}
					}
					if (addX)
					{
						colCoords.Add(item.Center().X);
					}
					if (addY)
					{
						rowCoords.Add(item.Center().Y);
					}
				}

				// Clear it all because we're going to use X,Y coordinate pairings to build rectangles
				// around. This won't be perfect but it should algorithmically put rectangles over all
				// images on the screen. The center of each of these rectangles should be a good enough
				// spot to click.
				rectangles.Clear();
				colCoords.Sort();
				rowCoords.Sort();
				foreach (var row in rowCoords)
				{
					foreach (var col in colCoords)
					{
						int x = (int)( col - (sWidth * .5) );
						int y = (int)( row - (sHeight * .5) );

						rectangles.Add(new Rectangle(x, y, sWidth, sHeight));
					}
				}

				// Remove some rectangles that somehow overlap each other. Don't think this happens
				// but it doesn't hurt to double check.
				for (int i = 0; i < rectangles.Count - 1; i++)
				{
					for (int j = i + 1; j < rectangles.Count; j++)
					{
						Rectangle r1 = rectangles[i];
						Rectangle r2 = rectangles[j];
						Rectangle intersect = Rectangle.Intersect(r1, r2);
						if (intersect.Width > r1.Width * .2)
						{
							rectangles.RemoveAt(j);
						}
					}
				}

				// Sort by row then by column within each row
				rectangles = rectangles.OrderBy(r => r.Top).ThenBy(r => r.Left).ToList();

				Debug.WriteLine($"{colCoords.Count} columns");
				Debug.WriteLine($"{rowCoords.Count} rows");
				Debug.WriteLine($"{rectangles.Count} rectangles");

				// Generated rectangles
				new RectanglesMarker(rectangles, Color.Green).ApplyInPlace(output);
				//Navigation.DisplayBitmap(output, "Rectangles");

				if (colCoords.Count < 7 || rowCoords.Count < 5)
				{
					output.Save($"./logging/weapons/WeaponInventory_{colCoords.Count}x{rowCoords.Count}.png");
				}

				screenshot.Dispose();
				output.Dispose();
				return (rectangles, colCoords.Count, rowCoords.Count);
			}
		}

		public static void QueueScan(int id)
		{
			int width = Navigation.GetWidth();
			int height = Navigation.GetHeight();

			// Separate to all pieces of card
			List<Bitmap> weaponImages = new List<Bitmap>();

			Bitmap card;
			RECT reference;
			Bitmap name, level, refinement, equipped;

			if (Navigation.GetAspectRatio() == new Size(16, 9))
			{
				// Grab image of entire card on Right
				reference = new RECT(new Rectangle(862, 80, 327, 560)); // In 1280x720

				int left   = (int)Math.Round(reference.Left   / 1280.0 * width, MidpointRounding.AwayFromZero);
				int top    = (int)Math.Round(reference.Top    / 720.0 * height, MidpointRounding.AwayFromZero);
				int right  = (int)Math.Round(reference.Right  / 1280.0 * width, MidpointRounding.AwayFromZero);
				int bottom = (int)Math.Round(reference.Bottom / 720.0 * height, MidpointRounding.AwayFromZero);

				card = Navigation.CaptureRegion(new RECT(left, top, right, bottom));

				// Equipped Character
				equipped = card.Clone(new RECT(
				Left: (int)( 52.0 / reference.Width * card.Width ),
				Top: (int)( 522.0 / reference.Height * card.Height ),
				Right: card.Width,
				Bottom: card.Height), card.PixelFormat);
			}
			else // if (Navigation.GetAspectRatio() == new Size(8, 5))
			{
				// Grab image of entire card on Right
				reference = new Rectangle(862, 80, 328, 640); // In 1280x800

				int left   = (int)Math.Round(reference.Left   / 1280.0 * width, MidpointRounding.AwayFromZero);
				int top    = (int)Math.Round(reference.Top    / 800.0 * height, MidpointRounding.AwayFromZero);
				int right  = (int)Math.Round(reference.Right  / 1280.0 * width, MidpointRounding.AwayFromZero);
				int bottom = (int)Math.Round(reference.Bottom / 800.0 * height, MidpointRounding.AwayFromZero);

				RECT itemCard = new RECT(left, top, right, bottom);

				card = Navigation.CaptureRegion(itemCard);

				// Equipped Character
				equipped = card.Clone(new RECT(
					Left: (int)( 52.0 / reference.Width * card.Width ),
					Top: (int)( 602.0 / reference.Height * card.Height ),
					Right: card.Width,
					Bottom: card.Height), card.PixelFormat);
			}

			// Name
			name = card.Clone(new RECT(
				Left: 0,
				Top: 0,
				Right: card.Width,
				Bottom: (int)( 38.0 / reference.Height * card.Height )), card.PixelFormat);

			// Level
			level = card.Clone(new RECT(
				Left: (int)( 19.0 / reference.Width * card.Width ),
				Top: (int)( 206.0 / reference.Height * card.Height ),
				Right: (int)( 107.0 / reference.Width * card.Width ),
				Bottom: (int)( 225.0 / reference.Height * card.Height )), card.PixelFormat);

			// Refinement
			refinement = card.Clone(new RECT(
				Left: (int)( 20.0 / reference.Width * card.Width ),
				Top: (int)( 235.0 / reference.Height * card.Height ),
				Right: (int)( 40.0 / reference.Width * card.Width ),
				Bottom: (int)( 254.0 / reference.Height * card.Height )), card.PixelFormat);

			// Assign to List
			weaponImages.Add(name);
			weaponImages.Add(level);
			weaponImages.Add(refinement);
			weaponImages.Add(equipped);
			weaponImages.Add(card);

			try
			{
				int rarity = GetRarity(name);
				if (0 < rarity && rarity < Properties.Settings.Default.MinimumWeaponRarity)
				{
					weaponImages.ForEach(i => i.Dispose());
					StopScanning = true;
					return;
				}
				else // Send images to worker queue
					InventoryKamera.workerQueue.Enqueue(new OCRImage(weaponImages, "weapon", id));
			}
			catch (Exception ex)
			{
				UserInterface.AddError($"Unexpected error {ex.Message} for weapon ID#{id}");
				UserInterface.AddError($"{ex.StackTrace}");
				card.Save($"./logging/weapons/weapon{id}.png");
			}
		}

		public static async Task<Weapon> CatalogueFromBitmapsAsync(List<Bitmap> bm, int id)
		{
			// Init Variables
			string name = null;
			int level = -1;
			bool ascended = false;
			int refinementLevel = -11;
			string equippedCharacter = null;
			int rarity = 0;

			if (bm.Count >= 4)
			{
				int w_name = 0; int w_level = 1; int w_refinement = 2; int w_equippedCharacter = 3;

				// Check for Rarity
				rarity = GetRarity(bm[w_name]);

				// Check for equipped color
				Color equippedColor = Color.FromArgb(255, 255, 231, 187);
				Color equippedStatus = bm[w_equippedCharacter].GetPixel(5, 5);

				bool b_equipped = Scraper.CompareColors(equippedColor, equippedStatus);

				List<Task> tasks = new List<Task>();

				var taskName = Task.Run(() => name = ScanName(bm[w_name]));
				var taskLevel = Task.Run(() => level = ScanLevel(bm[w_level], ref ascended));
				var taskRefinement = Task.Run(() => refinementLevel = ScanRefinement(bm[w_refinement]));
				var taskEquipped = Task.Run(() => equippedCharacter = ScanEquippedCharacter(bm[w_equippedCharacter]));

				tasks.Add(taskName);
				tasks.Add(taskLevel);
				tasks.Add(taskRefinement);

				if (b_equipped)
				{
					tasks.Add(taskEquipped);
				}

				await Task.WhenAll(tasks.ToArray());
			}

			return new Weapon(name, level, ascended, refinementLevel, equippedCharacter, id, rarity);
		}

		private static int GetRarity(Bitmap bitmap)
		{
			int x = (int)(10/1280.0 * Navigation.GetWidth());
			int y = (int)(10/720.0 * Navigation.GetHeight());

			Color rarityColor = bitmap.GetPixel(x,y);

			Color fiveStar    = Color.FromArgb(255, 188, 105,  50);
			Color fourStar    = Color.FromArgb(255, 161,  86, 224);
			Color threeStar   = Color.FromArgb(255,  81, 127, 203);
			Color twoStar     = Color.FromArgb(255,  42, 143, 114);
			Color oneStar     = Color.FromArgb(255, 114, 119, 138);

			if (Scraper.CompareColors(fiveStar, rarityColor)) return 5;
			else if (Scraper.CompareColors(fourStar, rarityColor)) return 4;
			else if (Scraper.CompareColors(threeStar, rarityColor)) return 3;
			else if (Scraper.CompareColors(twoStar, rarityColor)) return 2;
			else if (Scraper.CompareColors(oneStar, rarityColor)) return 1;
			else return 0; //throw new ArgumentException("Unable to determine weapon rarity");
		}

		public static bool IsEnhancementMaterial(Bitmap nameBitmap)
		{
			string material = ScanEnchancementOreName(nameBitmap);
			return !string.IsNullOrWhiteSpace(material) && Scraper.enhancementMaterials.Contains(material.ToLower());
		}

		public static string ScanEnchancementOreName(Bitmap bm)
		{
			Scraper.SetGamma(0.2, 0.2, 0.2, ref bm);
			Bitmap n = Scraper.ConvertToGrayscale(bm);
			Scraper.SetInvert(ref n);

			// Analyze
			string name = Regex.Replace(Scraper.AnalyzeText(n).ToLower(), @"[\W]", string.Empty);
			name = Scraper.FindClosestMaterialName(name, 3);
			n.Dispose();

			return name;
		}

		#region Task Methods

		public static string ScanName(Bitmap bm)
		{
			Scraper.SetGamma(0.2, 0.2, 0.2, ref bm);
			Bitmap n = Scraper.ConvertToGrayscale(bm);
			Scraper.SetInvert(ref n);

			// Analyze
			string text = Regex.Replace(Scraper.AnalyzeText(n).ToLower(), @"[\W]", string.Empty);
			text = Scraper.FindClosestWeapon(text);

			n.Dispose();

			// Check in Dictionary
			return text;
		}

		public static int ScanLevel(Bitmap bm, ref bool ascended)
		{
			Bitmap n = Scraper.ConvertToGrayscale(bm);
			Scraper.SetInvert(ref n);

			string text = Scraper.AnalyzeText(n).Trim();
			n.Dispose();
			text = Regex.Replace(text, @"(?![\d/]).", string.Empty);

			if (text.Contains('/'))
			{
				string[] temp = text.Split(new[] { '/' }, 2);

				if (temp.Length == 2)
				{
					if (int.TryParse(temp[0], out int level) && int.TryParse(temp[1], out int maxLevel))
					{
						maxLevel = (int)Math.Round(maxLevel / 10.0, MidpointRounding.AwayFromZero) * 10;
						ascended = 20 <= level && level < maxLevel;
						return level;
					}
					else
					{
					}
				}
			}
			return -1;
		}

		public static int ScanRefinement(Bitmap bm)
		{
			using (Bitmap up = Scraper.ResizeImage(bm, bm.Width * 2, bm.Height * 2))
			{
				Bitmap n = Scraper.ConvertToGrayscale(up);
				Scraper.SetInvert(ref n);

				string text = Scraper.AnalyzeText(n).Trim();
				n.Dispose();
				text = Regex.Replace(text, @"[^\d]", string.Empty);

				// Parse Int
				if (int.TryParse(text, out int refinementLevel))
				{
					return refinementLevel;
				}
			}
			return -1;
		}

		public static string ScanEquippedCharacter(Bitmap bm)
		{
			Bitmap n = Scraper.ConvertToGrayscale(bm);
			Scraper.SetContrast(60.0, ref n);

			string extractedString = Scraper.AnalyzeText(n);
			n.Dispose();

			if (extractedString != "")
			{
				var regexItem = new Regex("Equipped:");
				if (regexItem.IsMatch(extractedString))
				{
					var name = extractedString.Split(':')[1];

					name = Regex.Replace(name, @"[\W]", string.Empty).ToLower();
					name = Scraper.FindClosestCharacterName(name);

					return name;
				}
			}
			// artifact has no equipped character
			return null;
		}

		#endregion Task Methods
	}
}