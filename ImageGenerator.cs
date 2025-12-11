using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.Fonts;
using Windows.Web.Http;

namespace bluetoothTest
{


    public class ImageGenerator
    {
        /// <summary>
        /// 16bit grayscale bitmap을 생성합니다.
        /// </summary>
        /// <param name="width">이미지 너비</param>
        /// <param name="height">이미지 높이</param>
        /// <param name="pixelData">16bit grayscale 픽셀 데이터 (0-65535 범위)</param>
        /// <returns>BMP 파일 바이트 배열</returns>
        public static byte[] Create16BitGrayBitmap(int width, int height, ushort[] pixelData)
        {
            if (pixelData.Length != width * height)
            {
                throw new ArgumentException("픽셀 데이터의 크기가 width * height와 일치하지 않습니다.");
            }

            int rowSize = ((width * 16 + 31) / 32) * 4; // 4바이트 정렬
            int pixelDataSize = rowSize * height;
            int fileSize = 54 + pixelDataSize; // BMP 헤더(14) + DIB 헤더(40) + 픽셀 데이터

            using (MemoryStream ms = new MemoryStream())
            using (BinaryWriter writer = new BinaryWriter(ms))
            {
                // BMP File Header (14 bytes)
                writer.Write((ushort)0x4D42); // "BM"
                writer.Write(fileSize);
                writer.Write((uint)0); // Reserved
                writer.Write((uint)54); // Pixel data offset

                // DIB Header (BITMAPINFOHEADER, 40 bytes)
                writer.Write((uint)40); // DIB header size
                writer.Write(width);
                writer.Write(height);
                writer.Write((ushort)1); // Color planes
                writer.Write((ushort)16); // Bits per pixel
                writer.Write((uint)0); // Compression (BI_RGB)
                writer.Write((uint)pixelDataSize);
                writer.Write((int)2835); // Horizontal resolution (pixels/meter)
                writer.Write((int)2835); // Vertical resolution (pixels/meter)
                writer.Write((uint)0); // Colors in palette
                writer.Write((uint)0); // Important colors

                // Pixel Data (bottom-to-top)
                for (int y = height - 1; y >= 0; y--)
                {
                    for (int x = 0; x < width; x++)
                    {
                        writer.Write(pixelData[y * width + x]);
                    }
                    
                    // 패딩 추가 (4바이트 정렬)
                    int padding = rowSize - (width * 2);
                    for (int p = 0; p < padding; p++)
                    {
                        writer.Write((byte)0);
                    }
                }

                return ms.ToArray();
            }
        }


        /// <summary>
        /// 텍스트의 렌더링 크기를 측정합니다.
        /// </summary>
        /// <param name="text">측정할 텍스트</param>
        /// <param name="fontFamily">폰트 패밀리</param>
        /// <param name="fontSize">폰트 크기</param>
        /// <returns>텍스트의 너비와 높이</returns>
        public static (float Width, float Height) MeasureText(string text, string fontFamily = "Arial", float fontSize = 24)
        {
            var font = SystemFonts.CreateFont(fontFamily, fontSize);
            var textOptions = new TextOptions(font);
            FontRectangle size = TextMeasurer.MeasureSize(text, textOptions);
            
            return (size.Width, size.Height);
        }

        /// <summary>
        /// 지정된 너비에 맞춰 텍스트를 줄바꿈하여 16bit grayscale bitmap을 생성합니다.
        /// </summary>
        /// <param name="text">그릴 텍스트</param>
        /// <param name="fontFamily">폰트 패밀리</param>
        /// <param name="fontSize">폰트 크기</param>
        /// <param name="width">이미지 너비 (텍스트가 이 너비에 맞춰 자동 줄바꿈됨)</param>
        /// <param name="padding">텍스트 주변 여백 (기본값: 10)</param>
        /// <param name="textColor">텍스트 색상 (0-65535, 0=검정, 65535=흰색)</param>
        /// <param name="backgroundColor">배경 색상 (0-65535, 0=검정, 65535=흰색)</param>
        /// <returns>16bit grayscale 픽셀 데이터와 이미지 높이</returns>
        public static (ushort[] PixelData, int Width, int Height) CreateBitmapWithWrappedText(
            string text, 
            string fontFamily = "Arial", 
            float fontSize = 24, 
            int width = 400,
            int padding = 10,
            ushort textColor = 65535, 
            ushort backgroundColor = 0)
        {
            var font = SystemFonts.CreateFont(fontFamily, fontSize);
            
            // 텍스트가 그려질 실제 너비 (여백 제외)
            float textAreaWidth = width - (padding * 2);
            
            // TextOptions 설정 (자동 줄바꿈)
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(padding, padding),
                WrappingLength = textAreaWidth,
                WordBreaking = WordBreaking.Standard
            };
            
            // 줄바꿈된 텍스트의 크기 측정
            FontRectangle textBounds = TextMeasurer.MeasureBounds(text, textOptions);
            
            // 이미지 높이 계산 (텍스트 높이 + 상하 여백)
            int height = (int)Math.Ceiling(textBounds.Height) + (padding * 2) + 10; // 약간의 추가 여백
            
            ushort[] pixels = new ushort[width * height];
            
            using (var image = new Image<L8>(width, height))
            {
                // 배경색 설정
                byte bgColor = (byte)(backgroundColor * 255 / 65535);
                image.Mutate(ctx => ctx.BackgroundColor(Color.FromPixel(new L8(bgColor))));
                
                // 텍스트 그리기 (자동 줄바꿈 적용)
                byte txtColor = (byte)(textColor * 255 / 65535);
                image.Mutate(ctx => ctx.DrawText(textOptions, text, Color.FromPixel(new L8(txtColor))));
                
                // 8bit grayscale를 16bit로 변환
                image.ProcessPixelRows(accessor =>
                {
                    for (int py = 0; py < height; py++)
                    {
                        Span<L8> pixelRow = accessor.GetRowSpan(py);
                        for (int px = 0; px < width; px++)
                        {
                            byte gray8 = pixelRow[px].PackedValue;
                            pixels[py * width + px] = (ushort)((gray8 * 65535) / 255);
                        }
                    }
                });
            }
            
            return (pixels, width, height);
        }
        
        /// <summary>
        /// 지정된 너비에 맞춰 텍스트를 줄바꿈하여 8bit grayscale bitmap을 생성합니다.
        /// </summary>
        /// <param name="text">그릴 텍스트</param>
        /// <param name="fontFamily">폰트 패밀리</param>
        /// <param name="fontSize">폰트 크기</param>
        /// <param name="width">이미지 너비 (텍스트가 이 너비에 맞춰 자동 줄바꿈됨)</param>
        /// <param name="padding">텍스트 주변 여백 (기본값: 10)</param>
        /// <param name="textColor">텍스트 색상 (0-255, 0=검정, 255=흰색)</param>
        /// <param name="backgroundColor">배경 색상 (0-255, 0=검정, 255=흰색)</param>
        /// <returns>8bit grayscale 픽셀 데이터와 이미지 높이</returns>
        public static (byte[] PixelData, int Width, int Height) CreateBitmapWithWrappedText8Bit(
            string text, 
            string fontFamily = "Arial", 
            float fontSize = 24, 
            int width = 400,
            int padding = 10,
            byte textColor = 255, 
            byte backgroundColor = 0)
        {
            var font = SystemFonts.CreateFont(fontFamily, fontSize);
            
            // 텍스트가 그려질 실제 너비 (여백 제외)
            float textAreaWidth = width - (padding * 2);
            
            // TextOptions 설정 (자동 줄바꿈)
            var textOptions = new RichTextOptions(font)
            {
                Origin = new PointF(padding, padding),
                WrappingLength = textAreaWidth,
                WordBreaking = WordBreaking.Standard
            };
            
            // 줄바꿈된 텍스트의 크기 측정
            FontRectangle textBounds = TextMeasurer.MeasureBounds(text, textOptions);
            
            // 이미지 높이 계산 (텍스트 높이 + 상하 여백)
            int height = (int)Math.Ceiling(textBounds.Height) + (padding * 2) + 10; // 약간의 추가 여백
    
            byte[] pixels = new byte[width * height];
    
            using (var image = new Image<L8>(width, height))
            {
                // 배경색 설정
                image.Mutate(ctx => ctx.Clear(Color.FromPixel(new L8(backgroundColor))));
                
                // 텍스트 그리기 (자동 줄바꿈 적용)
                image.Mutate(ctx => ctx.DrawText(textOptions, text, Color.FromPixel(new L8(textColor))));
                
                // 8bit grayscale 픽셀 데이터 추출
                image.ProcessPixelRows(accessor =>
                {
                    for (int py = 0; py < height; py++)
                    {
                        Span<L8> pixelRow = accessor.GetRowSpan(py);
                        for (int px = 0; px < width; px++)
                        {
                            pixels[py * width + px] = pixelRow[px].PackedValue;
                        }
                    }
                });
            }
    
            return (pixels, width, height);
        }

        public static byte[] Convert8BitTo4Bit(byte[] pixels8Bit, int width, int height)
        {
            // Convert 8-bit (0-255) to 4-bit (0-15) by dividing by 16
            int pixels4BitCount = (width * height + 1) / 2; // Two pixels per byte
            byte[] pixels4Bit = new byte[pixels4BitCount];

            for (int i = 0; i < width * height; i++)
            {
                byte value4Bit = (byte)(pixels8Bit[i] / 16); // Map 0-255 to 0-15

                if (i % 2 == 0)
                {
                    pixels4Bit[i / 2] = (byte)(value4Bit << 4); // High nibble
                }
                else
                {
                    pixels4Bit[i / 2] |= value4Bit; // Low nibble
                }
            }

            return pixels4Bit;
        }


        /// <summary>
        /// 8bit grayscale 픽셀 데이터를 BMP 파일로 저장합니다.
        /// </summary>
        /// <param name="filePath">저장할 파일 경로</param>
        /// <param name="width">이미지 너비</param>
        /// <param name="height">이미지 높이</param>
        /// <param name="pixelData">8bit grayscale 픽셀 데이터 (0-255 범위)</param>
        public static void Save8BitGrayBitmap(string filePath, int width, int height, byte[] pixelData)
        {
            if (pixelData.Length != width * height)
            {
                throw new ArgumentException("픽셀 데이터의 크기가 width * height와 일치하지 않습니다.");
            }

            using (var image = new Image<L8>(width, height))
            {
                image.ProcessPixelRows(accessor =>
                {
                    for (int y = 0; y < height; y++)
                    {
                        Span<L8> pixelRow = accessor.GetRowSpan(y);
                        for (int x = 0; x < width; x++)
                        {
                            pixelRow[x] = new L8(pixelData[y * width + x]);
                        }
                    }
                });

                image.SaveAsBmp(filePath);
            }
        }
    }
}
