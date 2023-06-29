using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PMD_Euclidien
{
    /// <summary>
    /// Logique d'interaction pour MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        List<Bitmap> imagesBmp = new List<Bitmap>();
        public MainWindow()
        {
            InitializeComponent();
        }

        private void button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Multiselect = true;
            openFile.DefaultExt = "png";
            openFile.Filter = "PNG (*.png)|*.png|JPEG (*.jpg;*jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|TIFF (*.tiff)|*.tiff";
            openFile.ShowDialog();
            if (openFile.FileNames.Length > 0)
            {
                foreach (string filename in openFile.FileNames)
                {
                    imagesBmp.Add(new Bitmap(filename));
                }
            }
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            int max = int.Parse(textBox.Text);
            int itStabilite = int.Parse(textBox2.Text);
            double prStabilite = double.Parse(textBox3.Text) / 100;
            string[] poidsStr = textBox4.Text.Split(';');
            double[] poids = Array.ConvertAll(poidsStr, poidStr => double.Parse(poidStr));
            int normeP = int.Parse(textBox5.Text);
            double seuilPoids = double.Parse(textBox6.Text);
            List<int[,]> imagesMat = bmpToMat(imagesBmp);
            for (int i = 1; i <= max; i++)
            {
                List<int[,]> imagesErodeInit = new List<int[,]>();
                List<int[,]> imagesDilateInit = new List<int[,]>();
                List<int[,]> imagesOuvertesStandards = new List<int[,]>();
                List<int[,]> imagesFermeesStandards = new List<int[,]>();
                ErosionDilatationInit(imagesMat, poids, normeP, seuilPoids, ref imagesErodeInit, ref imagesDilateInit, i);
                OuvertureFermetureStandard(imagesErodeInit, imagesDilateInit, poids, normeP, seuilPoids, ref imagesOuvertesStandards, ref imagesFermeesStandards, i);

                for (int k = 0; k < imagesBmp.Count; k++)
                {
                    Bitmap sortieErod = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    Bitmap sortieDilat = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    Bitmap sortieOuverteStandard = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    Bitmap sortieFermeeStandard = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    for (int x = 0; x < imagesBmp[k].Width; x++)
                    {
                        for (int y = 0; y < imagesBmp[k].Height; y++)
                        {
                            sortieErod.SetPixel(x, y, Color.FromArgb(imagesErodeInit[k][x, y], imagesErodeInit[k][x, y], imagesErodeInit[k][x, y]));
                            sortieDilat.SetPixel(x, y, Color.FromArgb(imagesDilateInit[k][x, y], imagesDilateInit[k][x, y], imagesDilateInit[k][x, y]));
                            sortieOuverteStandard.SetPixel(x, y, Color.FromArgb(imagesOuvertesStandards[k][x, y], imagesOuvertesStandards[k][x, y], imagesOuvertesStandards[k][x, y]));
                            sortieFermeeStandard.SetPixel(x, y, Color.FromArgb(imagesFermeesStandards[k][x, y], imagesFermeesStandards[k][x, y], imagesFermeesStandards[k][x, y]));
                        }
                    }
                    sortieErod.Save(".\\IMAGES\\Erod B_" + k + " ES_" + i + ".tiff");
                    sortieDilat.Save(".\\IMAGES\\Dilat B_" + k + " ES_" + i + ".tiff");
                    sortieOuverteStandard.Save(".\\IMAGES\\OuvertureStandard B_" + k + " ES_" + i + ".tiff");
                    sortieFermeeStandard.Save(".\\IMAGES\\FermetureStandard B_" + k + " ES_" + i + ".tiff");
                }

                List<int[,]> gNewFerme = new List<int[,]>();
                List<int[,]> gNewOuvert = new List<int[,]>();
                Reconstruction(imagesMat, imagesErodeInit, imagesDilateInit, poids, normeP, seuilPoids, itStabilite, prStabilite, ref gNewOuvert, ref gNewFerme);

                List<Bitmap> fermeBmp = new List<Bitmap>();
                List<Bitmap> ouvertBmp = new List<Bitmap>();
                for (int k = 0; k < imagesBmp.Count; k++)
                {
                    Bitmap sortieFerme = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    Bitmap sortieOuvert = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                    for (int x = 0; x < imagesBmp[k].Width; x++)
                    {
                        for (int y = 0; y < imagesBmp[k].Height; y++)
                        {
                            sortieFerme.SetPixel(x, y, Color.FromArgb(gNewFerme[k][x, y], gNewFerme[k][x, y], gNewFerme[k][x, y]));
                            sortieOuvert.SetPixel(x, y, Color.FromArgb(gNewOuvert[k][x, y], gNewOuvert[k][x, y], gNewOuvert[k][x, y]));
                        }
                    }
                    fermeBmp.Add(sortieFerme);
                    ouvertBmp.Add(sortieOuvert);
                    sortieFerme.Save(".\\IMAGES\\FermeReconstruction B_" + k + " ES_" + i + ".tiff");
                    sortieOuvert.Save(".\\IMAGES\\OuvertReconstruction B_" + k + " ES_" + i + ".tiff");
                }
            }
        }

        private void Reconstruction(List<int[,]> imagesMat, List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, double[] poids, int normeP, double seuilPoids, int itStabilite, double prStabilite, ref List<int[,]> gNewOuvert, ref List<int[,]> gNewFerme)
        {
            bool stopErod = false, stopDilat = false;
            List<int[,]> gLastFerme = new List<int[,]>();
            List<int[,]> gLastOuvert = new List<int[,]>();
            for (int k = 0; k < imagesMat.Count; k++)
            {
                gNewFerme.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                gNewOuvert.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                gLastOuvert.Add((int[,])imagesErodeInit[k].Clone());
                gLastFerme.Add((int[,])imagesDilateInit[k].Clone());
            }
            int toleranceStabilite = 0;
            if (itStabilite == 0) itStabilite = int.MaxValue;
            while (((!stopDilat) || (!stopErod)) && (toleranceStabilite < itStabilite))
            {
                stopErod = true; stopDilat = true;
                ErosionDilatation(imagesMat, gLastFerme, gLastOuvert, poids, normeP, seuilPoids, prStabilite, ref gNewFerme, ref gNewOuvert, ref stopErod, ref stopDilat);
                for (int k = 0; k < imagesMat.Count; k++)
                {
                    gLastOuvert[k] = (int[,])gNewOuvert[k].Clone();
                    gLastFerme[k] = (int[,])gNewFerme[k].Clone();
                }
                toleranceStabilite++;
            }
        }

        private void ErosionDilatation(List<int[,]> imagesMat, List<int[,]> gLastFerme, List<int[,]> gLastOuvert, double[] poids, int normeP, double seuilPoids, double prStabilite, ref List<int[,]> gNewFerme, ref List<int[,]> gNewOuvert, ref bool stopErod, ref bool stopDilat)
        {
            int ressemblanceOuverture = 0, ressemblanceFermeture = 0;
            for (int x = 0; x < imagesMat[0].GetLength(0); x++)
            {
                for (int y = 0; y < imagesMat[0].GetLength(1); y++)
                {
                    if ((x < 1) || (y < 1) || (x > imagesMat[0].GetLength(0) - 2) || (y > imagesMat[0].GetLength(1) - 2))
                    {
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            gNewFerme[k][x, y] = gLastFerme[k][x, y];
                            gNewOuvert[k][x, y] = gLastOuvert[k][x, y];
                        }
                    }
                    else
                    {
                        int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                        MinMaxVecteurs(gLastFerme, gLastOuvert, poids, normeP, seuilPoids, x, y, ref sMin, ref tMin, ref sMax, ref tMax);

                        if (Norme(imagesMat, x, y) < Norme(gLastFerme, sMin, tMin))
                        {
                            for (int i = 0; i < imagesMat.Count; i++) gNewFerme[i][x, y] = gLastFerme[i][sMin, tMin];
                        }
                        else if (Norme(imagesMat, x, y) > Norme(gLastFerme, sMin, tMin))
                        {
                            for (int i = 0; i < imagesMat.Count; i++) gNewFerme[i][x, y] = imagesMat[i][x, y];
                        }
                        else
                        {
                            int cpt = 0;
                            for (int k = 0; k < imagesMat.Count; k++)
                            {
                                if (imagesMat[k][x, y] < gLastFerme[k][sMin, tMin]) cpt++;
                                else if (imagesMat[k][x, y] > gLastFerme[k][sMin, tMin]) cpt--;
                            }
                            if (cpt > 0) for (int i = 0; i < imagesMat.Count; i++) gNewFerme[i][x, y] = gLastFerme[i][sMin, tMin];
                            else if (cpt < 0) for (int i = 0; i < imagesMat.Count; i++) gNewFerme[i][x, y] = imagesMat[i][x, y];
                            else
                            {
                                for (int k = 0; k < imagesMat.Count; k++)
                                {
                                    if (imagesMat[k][x, y] < gLastFerme[k][sMin, tMin])
                                    {
                                        for (int i = 0; i < imagesMat.Count; i++) gNewFerme[i][x, y] = gLastFerme[i][sMin, tMin];
                                        break;
                                    }
                                    else if (imagesMat[k][x, y] > gLastFerme[k][sMin, tMin])
                                    {
                                        for (int i = 0; i < imagesMat.Count; i++) gNewFerme[i][x, y] = imagesMat[i][x, y];
                                        break;
                                    }
                                    else if (k == imagesMat.Count - 1) for (int i = 0; i < imagesMat.Count; i++) gNewFerme[i][x, y] = imagesMat[i][x, y];
                                }
                            }

                        }

                        if (Norme(imagesMat, x, y) > Norme(gLastOuvert, sMax, tMax))
                        {
                            for (int i = 0; i < imagesMat.Count; i++) gNewOuvert[i][x, y] = gLastOuvert[i][sMax, tMax];
                        }
                        else if (Norme(imagesMat, x, y) < Norme(gLastOuvert, sMax, tMax))
                        {
                            for (int i = 0; i < imagesMat.Count; i++) gNewOuvert[i][x, y] = imagesMat[i][x, y];
                        }
                        else
                        {
                            int cpt = 0;
                            for (int k = 0; k < imagesMat.Count; k++)
                            {
                                if (imagesMat[k][x, y] > gLastOuvert[k][sMax, tMax]) cpt--;
                                else if (imagesMat[k][x, y] < gLastOuvert[k][sMax, tMax]) cpt++;
                            }
                            if (cpt > 0) for (int i = 0; i < imagesMat.Count; i++) gNewOuvert[i][x, y] = imagesMat[i][x, y];
                            else if (cpt < 0) for (int i = 0; i < imagesMat.Count; i++) gNewOuvert[i][x, y] = gLastOuvert[i][sMax, tMax];
                            else
                            {
                                for (int k = 0; k < imagesMat.Count; k++)
                                {
                                    if (imagesMat[k][x, y] > gLastOuvert[k][sMax, tMax])
                                    {
                                        for (int i = 0; i < imagesMat.Count; i++) gNewOuvert[i][x, y] = gLastOuvert[i][sMax, tMax];
                                        break;
                                    }
                                    else if (imagesMat[k][x, y] < gLastOuvert[k][sMax, tMax])
                                    {
                                        for (int i = 0; i < imagesMat.Count; i++) gNewOuvert[i][x, y] = imagesMat[i][x, y];
                                        break;
                                    }
                                    else if (k == imagesMat.Count - 1)
                                    {
                                        for (int i = 0; i < imagesMat.Count; i++) gNewOuvert[i][x, y] = imagesMat[i][x, y];
                                    }
                                }
                            }
                        }
                        int ressemblanceOuvertureLocale = 0, ressemblanceFermetureLocale = 0;
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            if (gNewFerme[k][x, y] != gLastFerme[k][x, y]) stopErod = false;
                            else ressemblanceFermetureLocale++;
                            if (gNewOuvert[k][x, y] != gLastOuvert[k][x, y]) stopDilat = false;
                            else ressemblanceOuvertureLocale++;
                        }
                        if (ressemblanceOuvertureLocale == imagesMat.Count) ressemblanceOuverture++;
                        if (ressemblanceFermetureLocale == imagesMat.Count) ressemblanceFermeture++;
                    }
                }
            }
            double tauxRessemblanceOuverture = (double)ressemblanceOuverture / (imagesMat[0].GetLength(0) * imagesMat[0].GetLength(1));
            double tauxRessemblanceFermeture = (double)ressemblanceFermeture / (imagesMat[0].GetLength(0) * imagesMat[0].GetLength(1));
            Console.WriteLine(tauxRessemblanceOuverture.ToString() + " " + tauxRessemblanceFermeture.ToString());
            stopDilat = true;
            stopErod = true;
        }

        private double Norme(List<int[,]> imagesMat, int x, int y)
        {
            double norme = 0;
            for (int k = 0; k < imagesMat.Count; k++) norme += imagesMat[k][x, y];
            return norme;
        }

        private void OuvertureFermetureStandard(List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, double[] poids, int normeP, double seuilPoids, ref List<int[,]> imagesOuvertesStandards, ref List<int[,]> imagesFermeesStandards, int i)
        {
            List<int[,]> imagesErodPrec = new List<int[,]>();
            List<int[,]> imagesDilatePrec = new List<int[,]>();
            for (int k = 0; k < imagesErodeInit.Count; k++)
            {
                imagesOuvertesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                imagesFermeesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                imagesErodPrec.Add((int[,])imagesDilateInit[k].Clone());
                imagesDilatePrec.Add((int[,])imagesErodeInit[k].Clone());
            }
            for (int elemStruct = 0; elemStruct < i; elemStruct++)
            {
                for (int x = 0; x < imagesErodeInit[0].GetLength(0); x++)
                {
                    for (int y = 0; y < imagesErodeInit[0].GetLength(1); y++)
                    {
                        if ((x < 1) || (y < 1) || (x > imagesErodeInit[0].GetLength(0) - 2) || (y > imagesErodeInit[0].GetLength(1) - 2))
                        {
                            for (int k = 0; k < imagesErodeInit.Count; k++)
                            {
                                imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][x, y];
                                imagesFermeesStandards[k][x, y] = imagesErodPrec[k][x, y];
                            }
                        }
                        else
                        {
                            int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                            MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, poids, normeP, seuilPoids, x, y, ref sMin, ref tMin, ref sMax, ref tMax);
                            for (int k = 0; k < imagesErodeInit.Count; k++)
                            {
                                imagesFermeesStandards[k][x, y] = imagesErodPrec[k][sMin, tMin];
                                imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                            }
                        }
                    }
                }
                for (int k = 0; k < imagesErodeInit.Count; k++)
                {
                    imagesErodPrec[k] = (int[,])imagesFermeesStandards[k].Clone();
                    imagesDilatePrec[k] = (int[,])imagesOuvertesStandards[k].Clone();
                }
            }
        }

        private void ErosionDilatationInit(List<int[,]> imagesMat, double[] poids, int normeP, double seuilPoids, ref List<int[,]> imagesErodeInit, ref List<int[,]> imagesDilateInit, int i)
        {
            List<int[,]> imagesErodPrec = new List<int[,]>();
            List<int[,]> imagesDilatePrec = new List<int[,]>();
            for (int k = 0; k < imagesMat.Count; k++)
            {
                imagesDilateInit.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                imagesErodeInit.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                imagesErodPrec.Add((int[,])imagesMat[k].Clone());
                imagesDilatePrec.Add((int[,])imagesMat[k].Clone());
            }
            for (int elemStruct = 0; elemStruct < i; elemStruct++)
            {
                for (int x = 0; x < imagesMat[0].GetLength(0); x++)
                {
                    for (int y = 0; y < imagesMat[0].GetLength(1); y++)
                    {
                        if ((x < 1) || (y < 1) || (x > imagesMat[0].GetLength(0) - 2) || (y > imagesMat[0].GetLength(1) - 2))
                        {
                            for (int k = 0; k < imagesMat.Count; k++)
                            {
                                imagesDilateInit[k][x, y] = imagesDilatePrec[k][x, y];
                                imagesErodeInit[k][x, y] = imagesErodPrec[k][x, y];
                            }
                        }
                        else
                        {
                            int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                            MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, poids, normeP, seuilPoids, x, y, ref sMin, ref tMin, ref sMax, ref tMax);
                            for (int k = 0; k < imagesMat.Count; k++)
                            {
                                imagesErodeInit[k][x, y] = imagesErodPrec[k][sMin, tMin];
                                imagesDilateInit[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                            }
                        }
                    }
                }
                for (int k = 0; k < imagesMat.Count; k++)
                {
                    imagesErodPrec[k] = (int[,])imagesErodeInit[k].Clone();
                    imagesDilatePrec[k] = (int[,])imagesDilateInit[k].Clone();
                }
            }
        }

        private void MinMaxVecteurs(List<int[,]> imagesErodPrec, List<int[,]> imagesDilatePrec, double[] poids, int normeP, double seuilPoids, int x, int y, ref int sMin, ref int tMin, ref int sMax, ref int tMax)
        {
            double[,] matVerod = new double[9, imagesErodPrec.Count];
            double[,] matVdilat = new double[9, imagesErodPrec.Count];
            for (int s = x - 1; s <= x + 1; s++)
            {
                for (int t = y - 1; t <= y + 1; t++)
                {
                    for (int k = 0; k < imagesErodPrec.Count; k++)
                    {
                        matVerod[3 * (s - (x - 1)) + t - (y - 1), k] = imagesErodPrec[k][s, t] * poids[k];
                        matVdilat[3 * (s - (x - 1)) + t - (y - 1), k] = imagesDilatePrec[k][s, t] * poids[k];
                    }
                }
            }

            double[] solIdealePosErod = new double[imagesErodPrec.Count];
            double[] solIdealeNegErod = new double[imagesErodPrec.Count];

            double[] solIdealePosDilat = new double[imagesErodPrec.Count];
            double[] solIdealeNegDilat = new double[imagesErodPrec.Count];

            for (int k = 0; k < imagesErodPrec.Count; k++)
            {
                double minErod = double.MaxValue, maxErod = double.MinValue;
                double minDilat = double.MaxValue, maxDilat = double.MinValue;
                for (int i = 0; i < 9; i++)
                {
                    if (matVerod[i, k] > maxErod) maxErod = matVerod[i, k];
                    if (matVerod[i, k] < minErod) minErod = matVerod[i, k];

                    if (matVdilat[i, k] > maxDilat) maxDilat = matVdilat[i, k];
                    if (matVdilat[i, k] < minDilat) minDilat = matVdilat[i, k];
                }
                if (poids[k] >= seuilPoids)
                {
                    solIdealePosErod[k] = maxErod;
                    solIdealeNegErod[k] = minErod;

                    solIdealePosDilat[k] = maxDilat;
                    solIdealeNegDilat[k] = minDilat;
                }
                else
                {
                    solIdealePosErod[k] = minErod;
                    solIdealeNegErod[k] = maxErod;

                    solIdealePosDilat[k] = minDilat;
                    solIdealeNegDilat[k] = maxDilat;
                }
            }

            double[] dPisErod = new double[9];
            double[] dNisErod = new double[9];

            double[] dPisDilat = new double[9];
            double[] dNisDilat = new double[9];

            if (normeP > 0)
            {
                for (int i = 0; i < 9; i++)
                {
                    for (int k = 0; k < imagesErodPrec.Count; k++)
                    {
                        dPisErod[i] += Math.Pow(Math.Abs(solIdealePosErod[k] - matVerod[i, k]), normeP);
                        dNisErod[i] += Math.Pow(Math.Abs(solIdealeNegErod[k] - matVerod[i, k]), normeP);

                        dPisDilat[i] += Math.Pow(Math.Abs(solIdealePosDilat[k] - matVdilat[i, k]), normeP);
                        dNisDilat[i] += Math.Pow(Math.Abs(solIdealeNegDilat[k] - matVdilat[i, k]), normeP);
                    }
                    dPisErod[i] = Math.Pow(dPisErod[i], 1 / normeP);
                    dNisErod[i] = Math.Pow(dNisErod[i], 1 / normeP);

                    dPisDilat[i] = Math.Pow(dPisDilat[i], 1 / normeP);
                    dNisDilat[i] = Math.Pow(dNisDilat[i], 1 / normeP);
                }
            }
            else
            {

                for (int i = 0; i < 9; i++)
                {
                    dPisErod[i] = double.MinValue;
                    dNisErod[i] = double.MinValue;

                    dPisDilat[i] = double.MinValue;
                    dNisDilat[i] = double.MinValue;
                    for (int k = 0; k < imagesErodPrec.Count; k++)
                    {
                        if (Math.Abs(solIdealePosErod[k] - matVerod[i, k]) > dPisErod[i]) dPisErod[i] = Math.Abs(solIdealePosErod[k] - matVerod[i, k]);
                        if (Math.Abs(solIdealeNegErod[k] - matVerod[i, k]) > dNisErod[i]) dNisErod[i] = Math.Abs(solIdealeNegErod[k] - matVerod[i, k]);

                        if (Math.Abs(solIdealePosDilat[k] - matVdilat[i, k]) > dPisDilat[i]) dPisDilat[i] = Math.Abs(solIdealePosDilat[k] - matVdilat[i, k]);
                        if (Math.Abs(solIdealeNegDilat[k] - matVdilat[i, k]) > dNisDilat[i]) dNisDilat[i] = Math.Abs(solIdealeNegDilat[k] - matVdilat[i, k]);
                    }
                }
            }

            if (normeP == 1)
            {
                double minErod = double.MaxValue, maxDilat = double.MinValue;
                for (int i = 0; i < 9; i++)
                {
                    if (dNisErod[i] < minErod)
                    {
                        minErod = dNisErod[i];
                        sMin = (x - 1) + (i / 3);
                        tMin = (y - 1) + (i % 3);
                    }

                    if (dNisDilat[i] > maxDilat)
                    {
                        maxDilat = dNisDilat[i];
                        sMax = (x - 1) + (i / 3);
                        tMax = (y - 1) + (i % 3);
                    }
                }
            }
            else if (normeP == 0)
            {
                double[] cErod = new double[9];

                double[] cDilat = new double[9];

                for (int i = 0; i < 9; i++)
                {
                    cErod[i] = dNisErod[i] / (dNisErod[i] + dPisErod[i]);

                    cDilat[i] = dNisDilat[i] / (dNisDilat[i] + dPisDilat[i]);
                }

                double minErod = double.MaxValue, maxDilat = double.MinValue;
                for (int i = 0; i < 9; i++)
                {
                    if (cErod[i] < minErod)
                    {
                        minErod = cErod[i];
                        sMin = (x - 1) + (i / 3);
                        tMin = (y - 1) + (i % 3);
                    }

                    if (cDilat[i] > maxDilat)
                    {
                        maxDilat = cDilat[i];
                        sMax = (x - 1) + (i / 3);
                        tMax = (y - 1) + (i % 3);
                    }
                }
            }
            else if (normeP == 2)
            {
                double minPisErod = double.MaxValue, minPisDilat = double.MaxValue;
                double maxNisErod = double.MinValue, maxNisDilat = double.MinValue;
                int minPisErodIdx = 0, minPisDilatIdx = 0, maxNisErodIdx = 0, maxNisDilatIdx = 0;
                for (int i = 0; i < 9; i++)
                {
                    if (dPisErod[i] < minPisErod)
                    {
                        minPisErod = dPisErod[i];
                        minPisErodIdx = i;
                    }
                    if (dNisErod[i] > maxNisErod)
                    {
                        maxNisErod = dNisErod[i];
                        maxNisErodIdx = i;
                    }

                    if (dPisDilat[i] < minPisDilat)
                    {
                        minPisDilat = dPisDilat[i];
                        minPisDilatIdx = i;
                    }
                    if (dNisDilat[i] > maxNisDilat)
                    {
                        maxNisDilat = dNisDilat[i];
                        maxNisDilatIdx = i;
                    }
                }

                double dPisStarErod = minPisErod, dNisStarErod = maxNisErod;
                double dPisPrimErod = dPisErod[maxNisErodIdx], dNisPrimErod = dNisErod[minPisErodIdx];

                double dPisStarDilat = minPisDilat, dNisStarDilat = maxNisDilat;
                double dPisPrimDilat = dPisDilat[maxNisDilatIdx], dNisPrimDilat = dNisDilat[minPisDilatIdx];

                double[] muUnErod = new double[9];
                double[] muDeuxErod = new double[9];

                double[] muUnDilat = new double[9];
                double[] muDeuxDilat = new double[9];

                for (int i = 0; i < 9; i++)
                {
                    if (dPisErod[i] < dPisStarErod)
                    {
                        muUnErod[i] = 1;
                    }
                    else if (dPisErod[i] >= dPisStarErod && dPisErod[i] <= dPisPrimErod)
                    {
                        muUnErod[i] = (dPisPrimErod - dPisErod[i]) / (dPisPrimErod - dPisStarErod);
                    }
                    else muUnErod[i] = 0;

                    if (dNisErod[i] > dNisStarErod)
                    {
                        muDeuxErod[i] = 1;
                    }
                    else if (dNisErod[i] >= dNisPrimErod && dNisErod[i] <= dNisStarErod)
                    {
                        muDeuxErod[i] = (dNisErod[i] - dNisPrimErod) / (dNisStarErod - dNisPrimErod);
                    }
                    else muDeuxErod[i] = 0;


                    if (dPisDilat[i] < dPisStarDilat)
                    {
                        muUnDilat[i] = 1;
                    }
                    else if (dPisDilat[i] >= dPisStarDilat && dPisDilat[i] <= dPisPrimDilat)
                    {
                        muUnDilat[i] = (dPisPrimDilat - dPisDilat[i]) / (dPisPrimDilat - dPisStarDilat);
                    }
                    else muUnDilat[i] = 0;

                    if (dNisDilat[i] > dNisStarDilat)
                    {
                        muDeuxDilat[i] = 1;
                    }
                    else if (dNisDilat[i] >= dNisPrimDilat && dNisDilat[i] <= dNisStarDilat)
                    {
                        muDeuxDilat[i] = (dNisDilat[i] - dNisPrimDilat) / (dNisStarDilat - dNisPrimDilat);
                    }
                    else muDeuxDilat[i] = 0;
                }

                double[] muErod = new double[9];
                double[] muDilat = new double[9];

                for (int i = 0; i < 9; i++)
                {
                    if (muUnErod[i] <= muDeuxErod[i]) muErod[i] = muUnErod[i];
                    else muErod[i] = muDeuxErod[i];

                    if (muUnDilat[i] <= muDeuxDilat[i]) muDilat[i] = muUnDilat[i];
                    else muDilat[i] = muDeuxDilat[i];
                }

                double minErod = double.MaxValue, maxDilat = double.MinValue;
                for (int i = 0; i < 9; i++)
                {
                    if (muErod[i] < minErod)
                    {
                        minErod = muErod[i];
                        sMin = (x - 1) + (i / 3);
                        tMin = (y - 1) + (i % 3);
                    }

                    if (muDilat[i] > maxDilat)
                    {
                        maxDilat = muDilat[i];
                        sMax = (x - 1) + (i / 3);
                        tMax = (y - 1) + (i % 3);
                    }
                }
            }
        }

        private List<int[,]> bmpToMat(List<Bitmap> imagesBmp)
        {
            List<int[,]> imagesMat = new List<int[,]>();
            for (int z = 0; z < imagesBmp.Count; z++)
            {
                imagesMat.Add(new int[imagesBmp[z].Width, imagesBmp[z].Height]);
                for (int x = 0; x < imagesBmp[z].Width; x++)
                {
                    for (int y = 0; y < imagesBmp[z].Height; y++)
                    {
                        Color pixelColor = imagesBmp[z].GetPixel(x, y);
                        imagesMat[z][x, y] = pixelColor.R;
                    }
                }
            }
            return imagesMat;
        }
    }
}
