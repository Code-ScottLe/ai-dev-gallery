﻿// Copyright (c) Microsoft Corporation. All rights reserved.
// Licensed under the MIT License.

using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media;
using Windows.Storage.Streams;

namespace AIDevGallery.Samples.SharedCode;

internal class BitmapFunctions
{
    private static readonly float[] Mean = [0.485f, 0.456f, 0.406f];
    private static readonly float[] StdDev = [0.229f, 0.224f, 0.225f];

    public static Bitmap ResizeBitmap(Bitmap originalBitmap, int newWidth, int newHeight)
    {
        Bitmap resizedBitmap = new(newWidth, newHeight);
        using (Graphics graphics = Graphics.FromImage(resizedBitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(originalBitmap, 0, 0, newWidth, newHeight);
        }

        return resizedBitmap;
    }

    public static Bitmap ResizeWithPadding(Bitmap originalBitmap, int targetWidth, int targetHeight)
    {
        // Determine the scaling factor to fit the image within the target dimensions
        float scale = Math.Min((float)targetWidth / originalBitmap.Width, (float)targetHeight / originalBitmap.Height);

        // Calculate the new width and height based on the scaling factor
        int scaledWidth = (int)(originalBitmap.Width * scale);
        int scaledHeight = (int)(originalBitmap.Height * scale);

        // Center the image within the target dimensions
        int offsetX = (targetWidth - scaledWidth) / 2;
        int offsetY = (targetHeight - scaledHeight) / 2;

        // Create a new bitmap with the target dimensions and a white background for padding
        Bitmap paddedBitmap = new(targetWidth, targetHeight);
        using (Graphics graphics = Graphics.FromImage(paddedBitmap))
        {
            graphics.Clear(Color.White); // Set background color for padding to white
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

            // Draw the scaled image onto the center of the new bitmap
            graphics.DrawImage(originalBitmap, offsetX, offsetY, scaledWidth, scaledHeight);
        }

        return paddedBitmap;
    }

    public static async Task<Bitmap> ResizeVideoFrameWithPadding(VideoFrame videoFrame, int targetWidth, int targetHeight)
    {
        // Convert VideoFrame to SoftwareBitmap (RGBA8 for compatibility)
        var softwareBitmap = SoftwareBitmap.Convert(videoFrame.SoftwareBitmap, BitmapPixelFormat.Rgba8);

        using (IRandomAccessStream stream = new InMemoryRandomAccessStream())
        {
            // Create a BitmapEncoder for JPEG or PNG
            BitmapEncoder encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId, stream);

            // Set the software bitmap
            encoder.SetSoftwareBitmap(softwareBitmap);

            // Determine the scaling factor
            float scale = Math.Min((float)targetWidth / softwareBitmap.PixelWidth, (float)targetHeight / softwareBitmap.PixelHeight);

            // Calculate new scaled dimensions
            int scaledWidth = (int)(softwareBitmap.PixelWidth * scale);
            int scaledHeight = (int)(softwareBitmap.PixelHeight * scale);

            // Calculate padding offsets (centering the image)
            int offsetX = (targetWidth - scaledWidth) / 2;
            int offsetY = (targetHeight - scaledHeight) / 2;

            // Apply transformations
            encoder.BitmapTransform.ScaledWidth = (uint)scaledWidth;
            encoder.BitmapTransform.ScaledHeight = (uint)scaledHeight;
            encoder.BitmapTransform.InterpolationMode = BitmapInterpolationMode.Fant;

            await encoder.FlushAsync();
            stream.Seek(0); // Reset stream position

            // Convert to System.Drawing.Bitmap
            using var tempBitmap = new Bitmap(stream.AsStream());
            Bitmap paddedBitmap = new(targetWidth, targetHeight);

            using (Graphics graphics = Graphics.FromImage(paddedBitmap))
            {
                graphics.Clear(Color.White); // White padding background
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                // Draw the resized image centered
                graphics.DrawImage(tempBitmap, offsetX, offsetY, scaledWidth, scaledHeight);
            }

            return paddedBitmap;
        }
    }

    public static Tensor<float> PreprocessBitmapForFaceDetection(Bitmap bitmap, Tensor<float> input)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        int stride = bmpData.Stride;
        IntPtr ptr = bmpData.Scan0;
        int bytes = Math.Abs(stride) * height;
        byte[] rgbValues = new byte[bytes];

        Marshal.Copy(ptr, rgbValues, 0, bytes);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * stride) + (x * 3);
                byte blue = rgbValues[index];
                byte green = rgbValues[index + 1];
                byte red = rgbValues[index + 2];

                // Convert to grayscale and normalize to [0,1]
                float gray = (0.299f * red + 0.587f * green + 0.114f * blue) / 255f;

                input[0, 0, y, x] = (gray - .442f) / .28f;
            }
        }

        bitmap.UnlockBits(bmpData);

        return input;
    }

    public static Tensor<float> PreprocessBitmapWithStdDev(Bitmap bitmap, Tensor<float> input)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        int stride = bmpData.Stride;
        IntPtr ptr = bmpData.Scan0;
        int bytes = Math.Abs(stride) * height;
        byte[] rgbValues = new byte[bytes];

        Marshal.Copy(ptr, rgbValues, 0, bytes);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * stride + x * 3;
                byte blue = rgbValues[index];
                byte green = rgbValues[index + 1];
                byte red = rgbValues[index + 2];

                input[0, 0, y, x] = ((red / 255f) - Mean[0]) / StdDev[0];
                input[0, 1, y, x] = ((green / 255f) - Mean[1]) / StdDev[1];
                input[0, 2, y, x] = ((blue / 255f) - Mean[2]) / StdDev[2];
            }
        }

        bitmap.UnlockBits(bmpData);

        return input;
    }

    public static Tensor<float> PreprocessBitmapWithoutStandardization(Bitmap bitmap, Tensor<float> input)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        BitmapData bmpData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        int stride = bmpData.Stride;
        IntPtr ptr = bmpData.Scan0;
        int bytes = Math.Abs(stride) * height;
        byte[] rgbValues = new byte[bytes];

        Marshal.Copy(ptr, rgbValues, 0, bytes);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * stride + x * 3;
                byte blue = rgbValues[index];
                byte green = rgbValues[index + 1];
                byte red = rgbValues[index + 2];

                input[0, 0, y, x] = red / 255f;
                input[0, 1, y, x] = green / 255f;
                input[0, 2, y, x] = blue / 255f;
            }
        }

        bitmap.UnlockBits(bmpData);

        return input;
    }

    public static Tensor<float> PreprocessBitmapForYOLO(Bitmap bitmap, Tensor<float> input)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;

        BitmapData bmpData = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        int stride = bmpData.Stride;
        IntPtr ptr = bmpData.Scan0;
        int bytes = Math.Abs(stride) * height;
        byte[] rgbValues = new byte[bytes];

        Marshal.Copy(ptr, rgbValues, 0, bytes);

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = y * stride + x * 3;
                byte blue = rgbValues[index];
                byte green = rgbValues[index + 1];
                byte red = rgbValues[index + 2];

                // The reason this needs its own function is because the variables are in different places in the input
                input[0, y, x, 0] = red / 255f;
                input[0, y, x, 1] = green / 255f;
                input[0, y, x, 2] = blue / 255f;
            }
        }

        bitmap.UnlockBits(bmpData);

        return input;
    }

    public static DenseTensor<float> PreprocessBitmapForObjectDetection(Bitmap bitmap, int paddedHeight, int paddedWidth)
    {
        int width = bitmap.Width;
        int height = bitmap.Height;
        DenseTensor<float> input = new([3, paddedHeight, paddedWidth]);

        BitmapData bmpData = bitmap.LockBits(new Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format24bppRgb);

        int stride = bmpData.Stride;
        IntPtr ptr = bmpData.Scan0;
        int bytes = Math.Abs(stride) * height;
        byte[] rgbValues = new byte[bytes];

        Marshal.Copy(ptr, rgbValues, 0, bytes);

        for (int y = paddedHeight - height; y < height; y++)
        {
            for (int x = paddedWidth - width; x < width; x++)
            {
                int index = (y - (paddedHeight - height)) * stride + (x - (paddedWidth - width)) * 3;
                byte blue = rgbValues[index];
                byte green = rgbValues[index + 1];
                byte red = rgbValues[index + 2];

                input[0, y, x] = blue - Mean[0];
                input[1, y, x] = green - Mean[1];
                input[2, y, x] = red - Mean[2];
            }
        }

        bitmap.UnlockBits(bmpData);

        return input;
    }

    public static BitmapImage RenderPredictions(Bitmap image, List<Prediction> predictions)
    {
        using Graphics g = Graphics.FromImage(image);
        float markerSize = (image.Width + image.Height) * 0.001f;
        using Pen pen = new(Color.Red, markerSize);
        using Brush brush = new SolidBrush(Color.White);
        using Font font = new("Arial", GetAdjustedFontsize(predictions));
        foreach (var p in predictions)
        {
            if (p == null || p.Box == null)
            {
                continue;
            }

            // Draw the box
            g.DrawLine(pen, p.Box.Xmin, p.Box.Ymin, p.Box.Xmax, p.Box.Ymin);
            g.DrawLine(pen, p.Box.Xmax, p.Box.Ymin, p.Box.Xmax, p.Box.Ymax);
            g.DrawLine(pen, p.Box.Xmax, p.Box.Ymax, p.Box.Xmin, p.Box.Ymax);
            g.DrawLine(pen, p.Box.Xmin, p.Box.Ymax, p.Box.Xmin, p.Box.Ymin);

            string labelText = $"{p.Label}, {p.Confidence:0.00}";
            g.DrawString(labelText, font, brush, new PointF(p.Box.Xmin, p.Box.Ymin));
        }

        // returns bitmap image
        BitmapImage bitmapImage = new();
        using (MemoryStream memoryStream = new())
        {
            image.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);

            memoryStream.Position = 0;

            bitmapImage.SetSource(memoryStream.AsRandomAccessStream());
        }

        return bitmapImage;
    }

    public static BitmapImage? RenderBackgroundMask(Bitmap image, byte[] backgroundMask, int originalImageWidth, int originalImageHeight)
    {
        if (image == null || backgroundMask == null || backgroundMask.Length == 0)
        {
            return null;
        }

        using Graphics g = Graphics.FromImage(image);

        using SolidBrush semiTransparentRedBrush = new SolidBrush(Color.FromArgb(100, 255, 0, 0));

        for (int y = 0; y < originalImageHeight; y++)
        {
            for (int x = 0; x < originalImageWidth; x++)
            {
                int index = (y * originalImageWidth + x) * 4;
                if (backgroundMask[index + 3] > 128)
                {
                    g.FillRectangle(semiTransparentRedBrush, x, y, 1, 1);
                }
            }
        }

        BitmapImage bitmapImage = new();
        using (MemoryStream memoryStream = new())
        {
            image.Save(memoryStream, System.Drawing.Imaging.ImageFormat.Png);
            memoryStream.Position = 0;
            bitmapImage.SetSource(memoryStream.AsRandomAccessStream());
        }

        return bitmapImage;
    }

    // For super resolution
    public static Bitmap CropAndScale(Bitmap paddedBitmap, int originalWidth, int originalHeight, int modelScalingFactor)
    {
        float scale = Math.Min(128f / originalWidth, 128f / originalHeight);

        // Calculate the dimensions of the cropped area
        int cropWidth = (int)(originalWidth * scale * modelScalingFactor);
        int cropHeight = (int)(originalHeight * scale * modelScalingFactor);

        // Calculate the offset to locate the padded content in the 512x512 image
        int offsetX = (paddedBitmap.Width - cropWidth) / 2;
        int offsetY = (paddedBitmap.Height - cropHeight) / 2;

        // Crop the region containing the actual content
        Rectangle cropArea = new(offsetX, offsetY, cropWidth, cropHeight);
        using Bitmap croppedBitmap = paddedBitmap.Clone(cropArea, paddedBitmap.PixelFormat);

        // Scale the cropped bitmap to {modelScalingFactor} times the original image dimensions
        int finalWidth = originalWidth * modelScalingFactor;
        int finalHeight = originalHeight * modelScalingFactor;
        Bitmap scaledBitmap = new(finalWidth, finalHeight);

        using (Graphics graphics = Graphics.FromImage(scaledBitmap))
        {
            graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            graphics.DrawImage(croppedBitmap, 0, 0, finalWidth, finalHeight);
        }

        return scaledBitmap;
    }

    public static Bitmap TensorToBitmap(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> tensor)
    {
        // Assumes output tensor shape [batch, c, w, h]
        var outputTensor = tensor[0].AsTensor<float>();
        int height = outputTensor.Dimensions[2];
        int width = outputTensor.Dimensions[3];

        // Create the bitmap
        Bitmap bitmap = new(width, height, PixelFormat.Format24bppRgb);
        BitmapData bmpData = bitmap.LockBits(
            new Rectangle(0, 0, width, height),
            ImageLockMode.WriteOnly,
            PixelFormat.Format24bppRgb);

        int stride = bmpData.Stride;
        IntPtr ptr = bmpData.Scan0;
        byte[] pixelData = new byte[Math.Abs(stride) * height];

        // Fill the pixel data
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                int index = (y * stride) + (x * 3);

                // Extract RGB values from the tensor (assume [0,1] range)
                float rVal = outputTensor[0, 0, y, x];  // Red
                float gVal = outputTensor[0, 1, y, x];  // Green
                float bVal = outputTensor[0, 2, y, x];  // Blue

                // Scale to [0, 255] and clamp
                byte r = (byte)Math.Clamp(rVal * 255, 0, 255);
                byte g = (byte)Math.Clamp(gVal * 255, 0, 255);
                byte b = (byte)Math.Clamp(bVal * 255, 0, 255);

                // Store pixel values in BGR order
                pixelData[index] = b;
                pixelData[index + 1] = g;
                pixelData[index + 2] = r;
            }
        }

        // Copy the pixel data to the bitmap
        Marshal.Copy(pixelData, 0, ptr, pixelData.Length);
        bitmap.UnlockBits(bmpData);

        return bitmap;
    }

    // Crops bitmap given a prediciton box
    public static Bitmap CropImage(Bitmap originalImage, Box box)
    {
        int xmin = Math.Max(0, (int)box.Xmin);
        int ymin = Math.Max(0, (int)box.Ymin);
        int width = Math.Min(originalImage.Width - xmin, (int)(box.Xmax - box.Xmin));
        int height = Math.Min(originalImage.Height - ymin, (int)(box.Ymax - box.Ymin));

        Rectangle cropRectangle = new(xmin, ymin, width, height);
        return originalImage.Clone(cropRectangle, originalImage.PixelFormat);
    }

    // Overlays cropped section a bitmap inside the original image in the Box region
    public static Bitmap OverlayImage(Bitmap originalImage, Bitmap overlay, Box box)
    {
        using Graphics graphics = Graphics.FromImage(originalImage);

        // Scale the overlay to match the bounding box size
        graphics.DrawImage(overlay, new Rectangle(
            (int)box.Xmin,
            (int)box.Ymin,
            (int)(box.Xmax - box.Xmin),
            (int)(box.Ymax - box.Ymin)));

        return originalImage;
    }

    public static BitmapImage ConvertBitmapToBitmapImage(Bitmap bitmap)
    {
        using var stream = new InMemoryRandomAccessStream();

        // Save the bitmap to a stream
        bitmap.Save(stream.AsStream(), ImageFormat.Png);
        stream.Seek(0);

        // Create a BitmapImage from the stream
        BitmapImage bitmapImage = new();
        bitmapImage.SetSource(stream);

        return bitmapImage;
    }

    private static float GetAdjustedFontsize(List<Prediction> predictions)
    {
        float adjustedFontSize = 12;

        if (predictions.Count > 0)
        {
            int maxPredictionTextLength = predictions.Select(p => p.Label.Length).ToList().Max() + 5;
            float minPredictionBoxWidth = predictions.Select(p => p.Box!.Xmax - p.Box!.Xmin).ToList().Min();
            adjustedFontSize = Math.Clamp(minPredictionBoxWidth / ((float)maxPredictionTextLength), 8, 16);
        }

        return adjustedFontSize;
    }
}