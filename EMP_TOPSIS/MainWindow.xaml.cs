// corrigé le 25/06/2020
// Version V3 : élément structurant (ES) = disque euclidien de rayon i (dx² + dy² <= i²),
//   utilisé directement en une seule passe pour l'érosion, la dilatation, l'ouverture et
//   la fermeture (5, 13, 29, 49, 81 pixels pour i = 1..5, comme skimage.morphology.disk(i)).
//   La reconstruction géodésique utilise le disque élémentaire B1 (rayon 1, 5 pixels).
// Version V4 : [V4-ANTI-CONDORCET] le ∧ / ∨ point à point de la reconstruction utilise un ordre de référence
//   fixe et transitif (score de la méthode contre un panel fixe de vecteurs de l'image), et la suite
//   des marqueurs est rendue monotone : plus de cycle de Condorcet, stabilité toujours atteinte.
//   Le classement des n pixels de la fenêtre (MinMaxVecteurs) est inchangé.
// Version V5 : [V5] ensemble de référence de l'ordre anti-Condorcet = TOUS les pixels de l'image, calcul exact bande par bande
//   à partir des histogrammes (la V4 utilisait un échantillon de 256 pixels).
// =====================================================================================
// EMP_TOPSIS (paramètre p = 1 ou p = 2)
// Morphologie mathématique multivaluée (images multibandes) avec ordonnancement vectoriel
// par la méthode multicritère TOPSIS (distance de Minkowski L_p, p = 1 : Manhattan, p = 2 : euclidienne).
//
// Transformations calculées pour chaque taille i = 1..max de l'élément structurant (ES) :
//   érosion, dilatation, ouverture, fermeture, ouverture par reconstruction,
//   fermeture par reconstruction (+ export multibande ENVI .hdr).
//
// ES : disque euclidien de rayon i (dx² + dy² <= i²), utilisé en une seule passe (V3) :
//      tous les pixels du disque sont comparés ensemble. Reconstruction : disque B1 (5 pixels).
//
// Relation d'ordre total utilisée (voir la description de TOPSIS) :
//   a_i <= a_i'  <=>  d_p(a_i) < d_p(a_i')
//                 ou (d_p(a_i) = d_p(a_i') et f_l(a_i) < f_l(a_i'))
//                 ou (d_p(a_i) = d_p(a_i') et f_l(a_i) = f_l(a_i') et i <= i')
//   où d_p est le coefficient de proximité relative à la solution idéale, f_l la valeur normalisée
//   pondérée sur le critère (bande) de poids maximal, et i l'indice spatial du pixel-vecteur.
//   Supremum = pixel-vecteur maximal de cet ordre ; infimum = pixel-vecteur minimal.
// =====================================================================================
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Windows;
using SDColor = System.Drawing.Color;
using SDPoint = System.Drawing.Point;

namespace EMP_TOPSIS
{
    public partial class MainWindow : Window
    {
        // Tolérance numérique pour comparer deux nombres réels (coefficients de proximité, valeurs f_l)
        private const double EPS = 1e-12;

        // Tolérance pour le test de polarité w_j >= tau (évite qu'un poids égal à 1/m, à l'erreur
        // d'arrondi près, soit classé "coût" : cas de poids uniformes)
        private const double TOL_POLARITE = 1e-9;

        // Bandes chargées par l'utilisateur (une image panchromatique = une bande)
        List<Bitmap> imagesBmp = new List<Bitmap>();

        public MainWindow()
        {
            InitializeComponent();
        }

        // Dossier de sortie « IMAGES-résultat » : toujours juste sous le dossier du projet.
        // On remonte depuis le dossier de l'exécutable (bin\Debug\... ou Executable\) jusqu'au
        // dossier qui contient la solution (.sln) ; s'il n'y en a pas (exécutable copié seul sur
        // une autre machine), le dossier est créé à côté de l'exécutable.
        private static string DossierResultats()
        {
            string dossierExe = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                DirectoryInfo d = new DirectoryInfo(dossierExe);
                while (d != null)
                {
                    if (d.GetFiles("*.sln").Length > 0)
                        return Path.Combine(d.FullName, "IMAGES-résultat");
                    d = d.Parent;
                }
            }
            catch (Exception)
            {
                // Dossier parent illisible : on garde le dossier de l'exécutable
            }
            return Path.Combine(dossierExe, "IMAGES-résultat");
        }

        // ============================== CHARGEMENT DES BANDES ==============================
        private void button_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFile = new OpenFileDialog();
            openFile.Multiselect = true;
            openFile.DefaultExt = "png";
            openFile.Filter = "PNG (*.png)|*.png|JPEG (*.jpg;*.jpeg)|*.jpg;*.jpeg|BMP (*.bmp)|*.bmp|TIFF (*.tiff;*.tif)|*.tiff;*.tif";
            bool? ok = openFile.ShowDialog();
            if (ok != true || openFile.FileNames.Length == 0)
                return;
            // On libère les anciennes bandes avant d'en charger de nouvelles
            foreach (Bitmap old in imagesBmp)
                old.Dispose();
            imagesBmp.Clear();
            foreach (string filename in openFile.FileNames)
                imagesBmp.Add(new Bitmap(filename));
            MessageBox.Show(imagesBmp.Count + " bande(s) chargée(s).", "Chargement");
        }

        // Lecture sécurisée d'un entier
        private static bool TryParseInt(string s, out int v)
        {
            return int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out v)
                || int.TryParse((s ?? "").Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out v);
        }

        // Lecture sécurisée d'un réel (accepte la virgule ou le point décimal)
        private static bool TryParseDouble(string s, out double v)
        {
            string t = (s ?? "").Trim().Replace(',', '.');
            return double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture, out v);
        }

        // ============================== PROGRAMME PRINCIPAL ==============================
        private void button1_Click(object sender, RoutedEventArgs e)
        {
            // ---- 1. Vérification des données saisies ----
            if (imagesBmp.Count == 0)
            {
                MessageBox.Show("Chargez d'abord au moins une bande (bouton Parcourir).", "Erreur");
                return;
            }
            int max, itStabilite, normeP;
            double prStabilitePct;
            if (!TryParseInt(textBox.Text, out max) || max < 1)
            {
                MessageBox.Show("Taille maximale de l'ES invalide (entier >= 1).", "Erreur");
                return;
            }
            if (!TryParseInt(textBox2.Text, out itStabilite) || itStabilite < 0)
            {
                MessageBox.Show("Nombre d'itérations de stabilité invalide (entier >= 0 ; 0 = illimité).", "Erreur");
                return;
            }
            if (!TryParseDouble(textBox3.Text, out prStabilitePct) || prStabilitePct < 0 || prStabilitePct > 100)
            {
                MessageBox.Show("Pourcentage de ressemblance invalide (0 à 100).", "Erreur");
                return;
            }
            double prStabilite = prStabilitePct / 100.0;

            // Poids des bandes (séparés par des points-virgules)
            string[] parts = (textBox4.Text ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
            double[] poids = new double[parts.Length];
            for (int p = 0; p < parts.Length; p++)
            {
                if (!TryParseDouble(parts[p], out poids[p]) || poids[p] < 0)
                {
                    MessageBox.Show("Poids invalides (nombres positifs ou nuls). Exemple : 0,45;0,35;0,20", "Erreur");
                    return;
                }
            }
            if (poids.Length != imagesBmp.Count)
            {
                MessageBox.Show("Il faut autant de poids que de bandes (" + imagesBmp.Count + " bande(s), " + poids.Length + " poids).", "Erreur");
                return;
            }
            // TOPSIS exige sum(w_k) = 1 (le seuil de polarité tau = 1/m en dépend) :
            // si la somme vaut 100 (ou autre), on normalise
            double sumW = 0;
            for (int p = 0; p < poids.Length; p++) sumW += poids[p];
            if (sumW <= 0)
            {
                MessageBox.Show("La somme des poids doit être strictement positive.", "Erreur");
                return;
            }
            if (Math.Abs(sumW - 1.0) > 1e-9)
                for (int p = 0; p < poids.Length; p++) poids[p] /= sumW;

            // Paramètre p de la distance de Minkowski : 1 (Manhattan) ou 2 (euclidienne)
            if (!TryParseInt(textBox5.Text, out normeP) || (normeP != 1 && normeP != 2))
            {
                MessageBox.Show("Paramètre p invalide. Utilisez 1 (Manhattan) ou 2 (Euclidienne).", "Erreur");
                return;
            }

            // Toutes les bandes doivent avoir la même taille
            int w0 = imagesBmp[0].Width, h0 = imagesBmp[0].Height;
            for (int k = 1; k < imagesBmp.Count; k++)
            {
                if (imagesBmp[k].Width != w0 || imagesBmp[k].Height != h0)
                {
                    MessageBox.Show("Toutes les bandes doivent avoir la même taille.", "Erreur");
                    return;
                }
            }

            // ---- 2. Dossier de sortie ----
            string outDir = DossierResultats();
            Directory.CreateDirectory(outDir);

            // ---- 3. Conversion des bitmaps en matrices d'entiers [x, y] (une matrice par bande) ----
            List<int[,]> imagesMat;
            try
            {
                imagesMat = bmpToMat(imagesBmp);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Impossible de lire les images : " + ex.Message, "Erreur");
                return;
            }

            try
            {
                // Listes de toutes les reconstructions (toutes tailles d'ES, toutes bandes) pour l'export ENVI
                List<int[,]> enviOuvert = new List<int[,]>();
                List<int[,]> enviFerme = new List<int[,]>();
                List<string> nomsOuvert = new List<string>();
                List<string> nomsFerme = new List<string>();

                // ---- 4. Boucle sur la taille i de l'ES (disque) ----
                for (int i = 1; i <= max; i++)
                {
                    List<int[,]> imagesErodeInit = new List<int[,]>();
                    List<int[,]> imagesDilateInit = new List<int[,]>();
                    List<int[,]> imagesOuvertesStandards = new List<int[,]>();
                    List<int[,]> imagesFermeesStandards = new List<int[,]>();

                    // Érosion et dilatation multivaluées de taille i
                    ErosionDilatationInit(imagesMat, poids, normeP, ref imagesErodeInit, ref imagesDilateInit, i);
                    // Ouverture et fermeture standard de taille i
                    OuvertureFermetureStandard(imagesErodeInit, imagesDilateInit, poids, normeP, ref imagesOuvertesStandards, ref imagesFermeesStandards, i);

                    // Sauvegarde des résultats (une image TIFF par bande)
                    for (int k = 0; k < imagesBmp.Count; k++)
                    {
                        Bitmap sortieErod = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        Bitmap sortieDilat = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        Bitmap sortieOuverteStandard = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        Bitmap sortieFermeeStandard = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        for (int x = 0; x < imagesBmp[k].Width; x++)
                            for (int y = 0; y < imagesBmp[k].Height; y++)
                            {
                                sortieErod.SetPixel(x, y, Gray(imagesErodeInit[k][x, y]));
                                sortieDilat.SetPixel(x, y, Gray(imagesDilateInit[k][x, y]));
                                sortieOuverteStandard.SetPixel(x, y, Gray(imagesOuvertesStandards[k][x, y]));
                                sortieFermeeStandard.SetPixel(x, y, Gray(imagesFermeesStandards[k][x, y]));
                            }
                        sortieErod.Save(Path.Combine(outDir, "Erod B_" + k + " ES_" + i + ".tiff"));
                        sortieDilat.Save(Path.Combine(outDir, "Dilat B_" + k + " ES_" + i + ".tiff"));
                        sortieOuverteStandard.Save(Path.Combine(outDir, "OuvertureStandard B_" + k + " ES_" + i + ".tiff"));
                        sortieFermeeStandard.Save(Path.Combine(outDir, "FermetureStandard B_" + k + " ES_" + i + ".tiff"));
                        sortieErod.Dispose();
                        sortieDilat.Dispose();
                        sortieOuverteStandard.Dispose();
                        sortieFermeeStandard.Dispose();
                    }

                    // Ouverture et fermeture par reconstruction (arrêt par stabilité)
                    List<int[,]> gNewFerme = new List<int[,]>();
                    List<int[,]> gNewOuvert = new List<int[,]>();
                    Reconstruction(imagesMat, imagesErodeInit, imagesDilateInit, poids, normeP, itStabilite, prStabilite, ref gNewOuvert, ref gNewFerme);

                    for (int k = 0; k < imagesBmp.Count; k++)
                    {
                        Bitmap sortieFerme = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        Bitmap sortieOuvert = new Bitmap(imagesBmp[k].Width, imagesBmp[k].Height);
                        for (int x = 0; x < imagesBmp[k].Width; x++)
                            for (int y = 0; y < imagesBmp[k].Height; y++)
                            {
                                sortieFerme.SetPixel(x, y, Gray(gNewFerme[k][x, y]));
                                sortieOuvert.SetPixel(x, y, Gray(gNewOuvert[k][x, y]));
                            }
                        sortieFerme.Save(Path.Combine(outDir, "FermeReconstruction B_" + k + " ES_" + i + ".tiff"));
                        sortieOuvert.Save(Path.Combine(outDir, "OuvertReconstruction B_" + k + " ES_" + i + ".tiff"));
                        sortieFerme.Dispose();
                        sortieOuvert.Dispose();
                        // Mémorisation pour les fichiers multibandes ENVI
                        enviOuvert.Add((int[,])gNewOuvert[k].Clone());
                        enviFerme.Add((int[,])gNewFerme[k].Clone());
                        nomsOuvert.Add("OuvertReconstruction B_" + k + " ES_" + i);
                        nomsFerme.Add("FermeReconstruction B_" + k + " ES_" + i);
                    }
                }

                // ---- 5. Fichiers multibandes ENVI (.img + .hdr) ----
                List<int[,]> enviTout = new List<int[,]>();
                List<string> nomsTout = new List<string>();
                enviTout.AddRange(enviOuvert);
                enviTout.AddRange(enviFerme);
                nomsTout.AddRange(nomsOuvert);
                nomsTout.AddRange(nomsFerme);
                WriteEnviMultiband(outDir, "EXTENDED-PROFIL-MOR-multibande", w0, h0, enviTout, nomsTout);
                WriteEnviMultiband(outDir, "OuvertReconstruction-multibande", w0, h0, enviOuvert, nomsOuvert);
                WriteEnviMultiband(outDir, "FermeReconstruction-multibande", w0, h0, enviFerme, nomsFerme);

                MessageBox.Show("Traitement terminé.\nRésultats (TIFF + ENVI .hdr) dans :\n" + outDir, "EMP_TOPSIS");
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur pendant le calcul :\n" + ex.Message, "Erreur");
            }
        }

        // Convertit une valeur entière en niveau de gris (borné à [0, 255])
        private static SDColor Gray(int v)
        {
            if (v < 0) v = 0;
            if (v > 255) v = 255;
            return SDColor.FromArgb(v, v, v);
        }

        // Écrit un fichier multibande ENVI (.img en BSQ, 8 bits + .hdr)
        private static void WriteEnviMultiband(string outDir, string baseName, int width, int height, List<int[,]> bands, List<string> bandNames)
        {
            if (bands == null || bands.Count == 0)
                return;
            string imgPath = Path.Combine(outDir, baseName + ".img");
            string hdrPath = Path.Combine(outDir, baseName + ".hdr");
            using (FileStream fs = new FileStream(imgPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                byte[] buf = new byte[width * height];
                for (int b = 0; b < bands.Count; b++)
                {
                    int idx = 0;
                    for (int y = 0; y < height; y++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            int v = bands[b][x, y];
                            if (v < 0) v = 0;
                            if (v > 255) v = 255;
                            buf[idx++] = (byte)v;
                        }
                    }
                    fs.Write(buf, 0, buf.Length);
                }
            }
            var names = new System.Text.StringBuilder();
            for (int i = 0; i < bandNames.Count; i++)
            {
                if (i > 0) names.Append(",\r\n ");
                names.Append(bandNames[i]);
            }
            string hdr =
                "ENVI\r\n" +
                "description = { EMP_TOPSIS, " + baseName + " }\r\n" +
                "samples = " + width + "\r\n" +
                "lines = " + height + "\r\n" +
                "bands = " + bands.Count + "\r\n" +
                "header offset = 0\r\n" +
                "file type = ENVI Standard\r\n" +
                "data type = 1\r\n" +
                "interleave = bsq\r\n" +
                "byte order = 0\r\n" +
                "band names = {\r\n " + names + "\r\n}\r\n";
            File.WriteAllText(hdrPath, hdr, System.Text.Encoding.ASCII);
        }

        // ============================== ÉLÉMENT STRUCTURANT DISQUE ==============================
        // Décalages (dx, dy) des pixels couverts par un disque de rayon r centré sur (0,0).
        // r=1 -> 5 pixels, r=2 -> 13 pixels, r=3 -> 29 pixels, r=4 -> 49 pixels, r=5 -> 81 pixels
        // (même ensemble de pixels que skimage.morphology.disk(r)).
        // L'ordre de parcours (dx puis dy croissants) définit l'INDICE SPATIAL des pixels de l'ES.
        private List<SDPoint> GetDiskOffsets(int r)
        {
            var offs = new List<SDPoint>();
            for (int dx = -r; dx <= r; dx++)
                for (int dy = -r; dy <= r; dy++)
                    if (dx * dx + dy * dy <= r * r) offs.Add(new SDPoint(dx, dy));
            return offs;
        }

        // Vrai si le disque centré en (x,y) déborde de l'image : pixel de bord, non traité
        // (il conserve sa valeur précédente)
        private bool IsBorder(int x, int y, int W, int H, List<SDPoint> offs)
        {
            foreach (var p in offs)
            {
                int nx = x + p.X, ny = y + p.Y;
                if (nx < 0 || ny < 0 || nx >= W || ny >= H) return true;
            }
            return false;
        }

        // =====================================================================================
        // [V4-ANTI-CONDORCET] ORDRE DE RÉFÉRENCE FIXE POUR LE ∧ / ∨ POINT À POINT DE LA RECONSTRUCTION
        // =====================================================================================
        // Méthode : TOPSIS.
        // Problème corrigé (V4) : le ∧ / ∨ point à point de la reconstruction comparait deux vecteurs seuls
        // (la méthode appliquée à l'ensemble {A, B}). Cette comparaison n'est pas transitive : elle admet des
        // cycles de Condorcet (A > B, B > C et C > A), et la reconstruction pouvait tourner en boucle sans
        // jamais se stabiliser.
        // Correction (V4) : chaque vecteur v reçoit un score FIXE, calculé avec la même méthode et les mêmes
        // paramètres, mais toujours contre le même ensemble de référence R.
        // [V5] R = TOUS les pixels de l'image d'entrée f (la V4 utilisait un échantillon de 256 pixels). Le
        // coefficient TOPSIS de v dans R ∪ {v} ne dépend que de la somme des carrés, du maximum et du minimum
        // de chaque bande de f : il est calculé EXACTEMENT, sans échantillonnage.
        //   c_R(v) = coefficient de proximité TOPSIS de v dans l'ensemble R ∪ {v} (même normalisation,
        //   même polarité des bandes, mêmes solutions idéales et même distance que CoefficientsTOPSIS).
        // Deux vecteurs sont comparés par ce score, puis par l'ordre lexicographique des bandes en cas
        // d'égalité : c'est un ordre TOTAL sur les valeurs (réflexif, antisymétrique, transitif), donc
        // aucun cycle de Condorcet n'est possible. Le classement des n pixels de la fenêtre
        // (MinMaxVecteurs) n'est PAS modifié : seul le ∧ / ∨ point à point de la reconstruction change.
        private int[] prioriteRef;                          // [V4-ANTI-CONDORCET] ordre des bandes pour le départage
        private Dictionary<int[], double> cacheScoreRef;    // [V4-ANTI-CONDORCET] score déjà calculé de chaque vecteur
        private double[] poidsRef;                          // [V4-ANTI-CONDORCET] poids des bandes
        private int normePRef;                              // [V4-ANTI-CONDORCET] paramètre p de la distance de Minkowski
        private double[] sommeCarresRef, maxRef, minRef;    // [V5] somme des carrés, maximum et minimum de chaque bande de f

        // [V4-ANTI-CONDORCET] Égalité exacte de deux vecteurs (clé du cache des scores)
        private sealed class ComparateurVecteurs : IEqualityComparer<int[]>
        {
            public bool Equals(int[] a, int[] b)
            {
                if (ReferenceEquals(a, b)) return true;
                if (a == null || b == null || a.Length != b.Length) return false;
                for (int i = 0; i < a.Length; i++)
                    if (a[i] != b[i]) return false;
                return true;
            }

            public int GetHashCode(int[] a)
            {
                unchecked
                {
                    int h = 17;
                    for (int i = 0; i < a.Length; i++) h = h * 31 + a[i];
                    return h;
                }
            }
        }

        // [V4-ANTI-CONDORCET] [V5] Préparation de l'ensemble de référence R = toute l'image f : statistiques de
        // chaque bande (somme des carrés, maximum, minimum) et ordre de départage des bandes.
        private void PreparerOrdreReference(List<int[,]> imagesMat, double[] poids, int normeP)
        {
            int m = imagesMat.Count;
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            // Départage : bandes par poids décroissant (à poids égaux, la bande d'indice le plus petit d'abord)
            prioriteRef = new int[m];
            for (int k = 0; k < m; k++) prioriteRef[k] = k;
            for (int a = 1; a < m; a++)   // tri par insertion (stable)
            {
                int bande = prioriteRef[a];
                int j = a - 1;
                while (j >= 0 && poids[prioriteRef[j]] < poids[bande])
                {
                    prioriteRef[j + 1] = prioriteRef[j];
                    j--;
                }
                prioriteRef[j + 1] = bande;
            }
            poidsRef = (double[])poids.Clone();
            normePRef = normeP;
            // [V5] Statistiques calculées sur TOUS les pixels de f (V4 : sur 256 pixels prélevés)
            sommeCarresRef = new double[m];
            maxRef = new double[m];
            minRef = new double[m];
            for (int k = 0; k < m; k++)
            {
                maxRef[k] = double.MinValue;
                minRef[k] = double.MaxValue;
                for (int x = 0; x < W; x++)
                    for (int y = 0; y < H; y++)
                    {
                        double g = imagesMat[k][x, y];
                        sommeCarresRef[k] += g * g;
                        if (g > maxRef[k]) maxRef[k] = g;
                        if (g < minRef[k]) minRef[k] = g;
                    }
            }
            cacheScoreRef = new Dictionary<int[], double>(new ComparateurVecteurs());
        }

        // [V4-ANTI-CONDORCET] [V5] Score fixe c_R(v) : coefficient de proximité TOPSIS de v dans l'ensemble R ∪ {v},
        // R = toute l'image (sommeCarresRef, maxRef et minRef portent sur tous les pixels de f)
        private double ScoreReference(int[] v)
        {
            double s;
            if (cacheScoreRef.TryGetValue(v, out s)) return s;
            int m = v.Length;
            double tau = 1.0 / m;
            double sP = 0, sN = 0;
            for (int k = 0; k < m; k++)
            {
                double norme = Math.Sqrt(sommeCarresRef[k] + (double)v[k] * v[k]);
                if (norme == 0) norme = 1;
                double fv = poidsRef[k] * v[k] / norme;
                double fmax = poidsRef[k] * Math.Max(maxRef[k], (double)v[k]) / norme;
                double fmin = poidsRef[k] * Math.Min(minRef[k], (double)v[k]) / norme;
                double pis, nis;
                if (poidsRef[k] >= tau - TOL_POLARITE) { pis = fmax; nis = fmin; }   // bande « bénéfice »
                else { pis = fmin; nis = fmax; }                                      // bande « coût »
                sP += Math.Pow(Math.Abs(pis - fv), normePRef);
                sN += Math.Pow(Math.Abs(nis - fv), normePRef);
            }
            double dPis = Math.Pow(sP, 1.0 / normePRef);
            double dNis = Math.Pow(sN, 1.0 / normePRef);
            double d = dPis + dNis;
            s = d < EPS ? 0.5 : dNis / d;
            if (double.IsNaN(s) || double.IsInfinity(s)) s = 0;   // sécurité : le score reste une fonction de v
            s = Math.Round(s, 10);                                 // arrondi fixe : ex aequo numériques => départage lexicographique
            cacheScoreRef[v] = s;
            return s;
        }

        // [V4-ANTI-CONDORCET] Comparaison de deux vecteurs pour le ∧ / ∨ point à point de la reconstruction :
        // score fixe (contre l'ensemble de référence R), puis ordre lexicographique des bandes (prioriteRef) en cas d'égalité.
        // C'est un ordre total sur les valeurs : il est transitif, aucun cycle de Condorcet n'est possible.
        // Retourne -1 si A < B, 0 si A et B sont le même vecteur, 1 si A > B.
        private int CompareReference(List<int[,]> imgA, int xa, int ya, List<int[,]> imgB, int xb, int yb)
        {
            int m = imgA.Count;
            int[] a = new int[m];
            int[] b = new int[m];
            for (int k = 0; k < m; k++)
            {
                a[k] = imgA[k][xa, ya];
                b[k] = imgB[k][xb, yb];
            }
            double sa = ScoreReference(a);
            double sb = ScoreReference(b);
            if (sa < sb) return -1;
            if (sa > sb) return 1;
            foreach (int k in prioriteRef)
            {
                if (a[k] < b[k]) return -1;
                if (a[k] > b[k]) return 1;
            }
            return 0;
        }


        // ============================== RECONSTRUCTION MORPHOLOGIQUE ==============================
        // Ouverture par reconstruction : R^delta_f( epsilon_Bi(f) )  (marqueur = érodé, masque = f)
        // Fermeture par reconstruction : R^epsilon_f( delta_Bi(f) )  (marqueur = dilaté, masque = f)
        // On itère les dilatations/érosions géodésiques d'ordre 1 jusqu'à stabilité, ou jusqu'à
        // atteindre le nombre d'itérations maximal fixé (0 = illimité), ou jusqu'au pourcentage de
        // ressemblance demandé, ou jusqu'à la détection d'un cycle (état déjà rencontré).
        private void Reconstruction(List<int[,]> imagesMat, List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, double[] poids, int normeP, int itStabilite, double prStabilite, ref List<int[,]> gNewOuvert, ref List<int[,]> gNewFerme)
        {
            List<int[,]> gLastFerme = new List<int[,]>();
            List<int[,]> gLastOuvert = new List<int[,]>();
            for (int k = 0; k < imagesMat.Count; k++)
            {
                gNewFerme.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                gNewOuvert.Add(new int[imagesMat[0].GetLength(0), imagesMat[0].GetLength(1)]);
                // Marqueurs initiaux : image érodée (ouverture) et image dilatée (fermeture)
                gLastOuvert.Add((int[,])imagesErodeInit[k].Clone());
                gLastFerme.Add((int[,])imagesDilateInit[k].Clone());
            }

            // [V4-ANTI-CONDORCET] Ordre de référence fixe (transitif) utilisé par le ∧ / ∨ point à point.
            PreparerOrdreReference(imagesMat, poids, normeP);
            // [V4-ANTI-CONDORCET] Marqueurs placés du bon côté du masque f (au sens de l'ordre de référence) :
            //   ouverture : h0 = inf(epsilon(f), f) ;  fermeture : h0 = sup(delta(f), f).
            // Avec la règle de monotonie de ErosionDilatationGeodesique, la suite des marqueurs est monotone
            // et bornée par f dans un ensemble fini de vecteurs : la stabilité est TOUJOURS atteinte.
            for (int x = 0; x < imagesMat[0].GetLength(0); x++)
                for (int y = 0; y < imagesMat[0].GetLength(1); y++)
                {
                    if (CompareReference(gLastOuvert, x, y, imagesMat, x, y) > 0)
                        for (int k = 0; k < imagesMat.Count; k++) gLastOuvert[k][x, y] = imagesMat[k][x, y];
                    if (CompareReference(gLastFerme, x, y, imagesMat, x, y) < 0)
                        for (int k = 0; k < imagesMat.Count; k++) gLastFerme[k][x, y] = imagesMat[k][x, y];
                }
            int toleranceStabilite = 0;
            if (itStabilite == 0) itStabilite = int.MaxValue; // 0 = nombre d'itérations non limité
            bool stopErod = false, stopDilat = false;

            // Détection de cycle : la comparaison TOPSIS de deux vecteurs isolés n'est pas transitive
            // (on peut avoir A > B > C > A). La suite des images géodésiques peut donc devenir périodique
            // (A -> B -> ... -> A) sans jamais se stabiliser. On mémorise l'empreinte de chaque état
            // rencontré ; si un état réapparaît, la stabilité ne sera jamais atteinte et on arrête la
            // transformation concernée (évite une boucle infinie avec 0 itération = illimité et 100 %).
            // [V4-ANTI-CONDORCET] Avec l'ordre de référence et la règle de monotonie, un état ne peut plus réapparaître ;
            // cette détection de cycle est conservée uniquement par sécurité (elle ne se déclenche plus).
            HashSet<ulong> etatsOuvert = new HashSet<ulong>();
            HashSet<ulong> etatsFerme = new HashSet<ulong>();
            etatsOuvert.Add(Empreinte(gLastOuvert));
            etatsFerme.Add(Empreinte(gLastFerme));
            bool cycleOuvert = false, cycleFerme = false;

            while (((!stopDilat) || (!stopErod)) && (toleranceStabilite < itStabilite))
            {
                stopErod = true; stopDilat = true;
                // Une étape de dilatation / érosion géodésique d'ordre 1
                ErosionDilatationGeodesique(imagesMat, gLastFerme, gLastOuvert, poids, normeP, prStabilite, ref gNewFerme, ref gNewOuvert, ref stopErod, ref stopDilat);
                // Les images calculées deviennent les marqueurs de l'itération suivante
                for (int k = 0; k < imagesMat.Count; k++)
                {
                    gLastOuvert[k] = (int[,])gNewOuvert[k].Clone();
                    gLastFerme[k] = (int[,])gNewFerme[k].Clone();
                }
                toleranceStabilite++;

                // Un état déjà rencontré => suite périodique => arrêt de la transformation concernée
                if (!etatsOuvert.Add(Empreinte(gLastOuvert))) cycleOuvert = true;
                if (!etatsFerme.Add(Empreinte(gLastFerme))) cycleFerme = true;
                if (cycleOuvert) stopDilat = true;
                if (cycleFerme) stopErod = true;
            }
        }

        // Empreinte 64 bits (FNV-1a) d'une image multibande, utilisée pour reconnaître un état déjà vu
        private static ulong Empreinte(List<int[,]> img)
        {
            unchecked
            {
                ulong h = 14695981039346656037UL;
                foreach (int[,] bande in img)
                    foreach (int v in bande)
                    {
                        h ^= (uint)v;
                        h *= 1099511628211UL;
                    }
                return h;
            }
        }

        // Une étape géodésique d'ordre 1 avec B1 (disque de rayon 1) :
        //   dilatation géodésique : delta_f^(1)(h)   = infimum ( delta_B1(h),   f )  -> ouverture
        //   érosion géodésique    : epsilon_f^(1)(h) = supremum( epsilon_B1(h), f )  -> fermeture
        // Le supremum/infimum de deux vecteurs est déterminé par l'ordre TOPSIS.
        // [V4-ANTI-CONDORCET] Le ∧ / ∨ point à point utilise CompareReference (ordre de référence fixe et transitif)
        // et la suite des marqueurs est monotone (voir la section ORDRE DE RÉFÉRENCE FIXE).
        private void ErosionDilatationGeodesique(List<int[,]> imagesMat, List<int[,]> gLastFerme, List<int[,]> gLastOuvert, double[] poids, int normeP, double prStabilite, ref List<int[,]> gNewFerme, ref List<int[,]> gNewOuvert, ref bool stopErod, ref bool stopDilat)
        {
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            var offsB1 = GetDiskOffsets(1);
            int ressemblanceOuverture = 0, ressemblanceFermeture = 0;
            for (int x = 0; x < W; x++)
            {
                for (int y = 0; y < H; y++)
                {
                    if (IsBorder(x, y, W, H, offsB1))
                    {
                        // Pixel de bord : valeur précédente conservée
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            gNewFerme[k][x, y] = gLastFerme[k][x, y];
                            gNewOuvert[k][x, y] = gLastOuvert[k][x, y];
                        }
                    }
                    else
                    {
                        // Infimum (érosion) du marqueur de fermeture et supremum (dilatation) du marqueur d'ouverture dans B1
                        int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                        MinMaxVecteurs(gLastFerme, gLastOuvert, poids, normeP, x, y, ref sMin, ref tMin, ref sMax, ref tMax, 1);

                        // Érosion géodésique : supremum( epsilon_B1(h), f )
                        // [V4-ANTI-CONDORCET] sup(epsilon_B1(h), f) au sens de l'ordre de référence fixe (transitif)
                        int cmpFerme = CompareReference(imagesMat, x, y, gLastFerme, sMin, tMin);
                        if (cmpFerme < 0) // f < epsilon_B1(h) => le supremum est epsilon_B1(h)
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = gLastFerme[k][sMin, tMin];
                        else              // f >= epsilon_B1(h) => le supremum est f
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = imagesMat[k][x, y];
                        // [V4-ANTI-CONDORCET] Monotonie : la fermeture par reconstruction est la limite d'une suite
                        // DÉCROISSANTE. Si la nouvelle valeur est plus grande que la précédente (au sens de l'ordre
                        // de référence), la valeur précédente est conservée : aucun aller-retour, donc aucun cycle.
                        if (CompareReference(gNewFerme, x, y, gLastFerme, x, y) > 0)
                            for (int k = 0; k < imagesMat.Count; k++) gNewFerme[k][x, y] = gLastFerme[k][x, y];

                        // Dilatation géodésique : infimum( delta_B1(h), f )
                        // [V4-ANTI-CONDORCET] inf(delta_B1(h), f) au sens de l'ordre de référence fixe (transitif)
                        int cmpOuvert = CompareReference(gLastOuvert, sMax, tMax, imagesMat, x, y);
                        if (cmpOuvert < 0) // delta_B1(h) < f => l'infimum est delta_B1(h)
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = gLastOuvert[k][sMax, tMax];
                        else               // delta_B1(h) >= f => l'infimum est f
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = imagesMat[k][x, y];
                        // [V4-ANTI-CONDORCET] Monotonie : l'ouverture par reconstruction est la limite d'une suite
                        // CROISSANTE. Si la nouvelle valeur est plus petite que la précédente (au sens de l'ordre
                        // de référence), la valeur précédente est conservée : aucun aller-retour, donc aucun cycle.
                        if (CompareReference(gNewOuvert, x, y, gLastOuvert, x, y) < 0)
                            for (int k = 0; k < imagesMat.Count; k++) gNewOuvert[k][x, y] = gLastOuvert[k][x, y];

                        // Test de stabilité : le pixel est-il identique à l'itération précédente (toutes bandes) ?
                        int locOuv = 0, locFerm = 0;
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            if (gNewFerme[k][x, y] != gLastFerme[k][x, y]) stopErod = false; else locFerm++;
                            if (gNewOuvert[k][x, y] != gLastOuvert[k][x, y]) stopDilat = false; else locOuv++;
                        }
                        if (locOuv == imagesMat.Count) ressemblanceOuverture++;
                        if (locFerm == imagesMat.Count) ressemblanceFermeture++;
                    }
                }
            }
            // Stabilité par pourcentage de ressemblance entre deux images successives
            double tauxOuv = (double)ressemblanceOuverture / (W * H);
            double tauxFerm = (double)ressemblanceFermeture / (W * H);
            if (tauxOuv >= prStabilite) stopDilat = true;
            if (tauxFerm >= prStabilite) stopErod = true;
        }

        // Compare deux pixels-vecteurs A(xa,ya) de imgA et B(xb,yb) de imgB au sens de l'ordre TOPSIS
        // appliqué à l'ensemble {A, B} (n = 2) : mêmes calculs que pour le voisinage complet
        // (fonction CoefficientsTOPSIS), puis relation d'ordre (d_p, f_l, indice spatial).
        // Retourne -1 si A < B, 0 si A = B, 1 si A > B.
        // [V4-ANTI-CONDORCET] Ancienne comparaison (versions précédentes), conservée pour mémoire : la reconstruction
        // ne l'appelle plus, elle utilise CompareReference (ordre de référence fixe et transitif).
        private int CompareDeuxVecteursTOPSIS(List<int[,]> imgA, int xa, int ya, List<int[,]> imgB, int xb, int yb, double[] poids, int p)
        {
            int m = imgA.Count;
            // Matrice de décision 2 x m : ligne 0 = A, ligne 1 = B
            double[,] g = new double[2, m];
            for (int k = 0; k < m; k++)
            {
                g[0, k] = imgA[k][xa, ya];
                g[1, k] = imgB[k][xb, yb];
            }
            double[] c = new double[2];
            double[] fl = new double[2];
            CoefficientsTOPSIS(g, 2, m, poids, p, c, fl);
            // Indice spatial des deux pixels (parcours x puis y)
            int H = imgA[0].GetLength(1);
            int idxA = xa * H + ya;
            int idxB = xb * H + yb;
            return ComparerOrdreTOPSIS(c[0], fl[0], idxA, c[1], fl[1], idxB);
        }

        // ============================== OUVERTURE / FERMETURE STANDARD ==============================
        // Ouverture = dilatation de l'érodé ; fermeture = érosion du dilaté (V3) : même ES
        //   (disque euclidien de rayon 'rayon'), appliqué en une seule passe à chaque opération.
        //   Pixel de bord (disque débordant de l'image) : valeur de l'image d'entrée conservée.
        private void OuvertureFermetureStandard(List<int[,]> imagesErodeInit, List<int[,]> imagesDilateInit, double[] poids, int normeP, ref List<int[,]> imagesOuvertesStandards, ref List<int[,]> imagesFermeesStandards, int rayon)
        {
            List<int[,]> imagesErodPrec = new List<int[,]>();
            List<int[,]> imagesDilatePrec = new List<int[,]>();
            for (int k = 0; k < imagesErodeInit.Count; k++)
            {
                imagesOuvertesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                imagesFermeesStandards.Add(new int[imagesErodeInit[0].GetLength(0), imagesErodeInit[0].GetLength(1)]);
                // Fermeture : on érode l'image dilatée ; ouverture : on dilate l'image érodée
                imagesErodPrec.Add((int[,])imagesDilateInit[k].Clone());
                imagesDilatePrec.Add((int[,])imagesErodeInit[k].Clone());
            }
            // V3 : ES = disque euclidien de rayon 'rayon' (pixels tels que dx² + dy² <= rayon²),
            // utilisé directement en une seule passe : tous les pixels du disque sont comparés ensemble.
            var offsBi = GetDiskOffsets(rayon);
            int W = imagesErodeInit[0].GetLength(0), H = imagesErodeInit[0].GetLength(1);
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    if (IsBorder(x, y, W, H, offsBi))
                    {
                        // Pixel de bord (le disque de rayon 'rayon' déborde de l'image) : valeur d'entrée conservée
                        for (int k = 0; k < imagesErodeInit.Count; k++)
                        {
                            imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][x, y];
                            imagesFermeesStandards[k][x, y] = imagesErodPrec[k][x, y];
                        }
                    }
                    else
                    {
                        int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                        MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, poids, normeP, x, y, ref sMin, ref tMin, ref sMax, ref tMax, rayon);
                        for (int k = 0; k < imagesErodeInit.Count; k++)
                        {
                            imagesFermeesStandards[k][x, y] = imagesErodPrec[k][sMin, tMin];
                            imagesOuvertesStandards[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                        }
                    }
                }
        }

        // ============================== ÉROSION / DILATATION ==============================
        // Érosion (infimum) et dilatation (supremum) multivaluées de taille 'rayon' (V3) :
        //   ES = disque euclidien de rayon 'rayon' ; pour chaque pixel, l'infimum et le supremum sont
        //   cherchés en UNE SEULE PASSE parmi TOUS les pixels-vecteurs couverts par ce disque.
        //   Pixel de bord (disque débordant de l'image) : valeur de l'image d'entrée conservée.
        private void ErosionDilatationInit(List<int[,]> imagesMat, double[] poids, int normeP, ref List<int[,]> imagesErodeInit, ref List<int[,]> imagesDilateInit, int rayon)
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
            // V3 : ES = disque euclidien de rayon 'rayon' (pixels tels que dx² + dy² <= rayon²),
            // utilisé directement en une seule passe : tous les pixels du disque sont comparés ensemble.
            var offsBi = GetDiskOffsets(rayon);
            int W = imagesMat[0].GetLength(0), H = imagesMat[0].GetLength(1);
            for (int x = 0; x < W; x++)
                for (int y = 0; y < H; y++)
                {
                    if (IsBorder(x, y, W, H, offsBi))
                    {
                        // Pixel de bord (le disque de rayon 'rayon' déborde de l'image) : valeur d'entrée conservée
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            imagesDilateInit[k][x, y] = imagesDilatePrec[k][x, y];
                            imagesErodeInit[k][x, y] = imagesErodPrec[k][x, y];
                        }
                    }
                    else
                    {
                        int sMax = 0, tMax = 0, sMin = 0, tMin = 0;
                        MinMaxVecteurs(imagesErodPrec, imagesDilatePrec, poids, normeP, x, y, ref sMin, ref tMin, ref sMax, ref tMax, rayon);
                        for (int k = 0; k < imagesMat.Count; k++)
                        {
                            imagesErodeInit[k][x, y] = imagesErodPrec[k][sMin, tMin];
                            imagesDilateInit[k][x, y] = imagesDilatePrec[k][sMax, tMax];
                        }
                    }
                }
        }

        // ============================== TOPSIS ==============================
        // Ordonne les n pixels-vecteurs du voisinage (disque de rayon 'rayonDisque' centré en (x,y)) :
        //   1) matrice normalisée pondérée f ; polarité des bandes (bénéfice si w >= 1/m, sinon coût)
        //   2) solutions idéales PIS (f*) et NIS (f-)
        //   3) distances d^PIS_p et d^NIS_p (Minkowski, p = 1 ou 2)
        //   4) coefficient de proximité d_p = d^NIS / (d^PIS + d^NIS)
        //   5) infimum = plus petit élément de l'ordre (érosion, calculé sur imagesErodPrec)
        //      supremum = plus grand élément de l'ordre (dilatation, calculé sur imagesDilatePrec)
        // Ordre : d_p, puis f_l (bande de poids maximal), puis indice spatial (voir ComparerOrdreTOPSIS).
        // Sorties : coordonnées (sMin,tMin) de l'infimum et (sMax,tMax) du supremum.
        private void MinMaxVecteurs(List<int[,]> imagesErodPrec, List<int[,]> imagesDilatePrec, double[] poids, int normeP, int x, int y, ref int sMin, ref int tMin, ref int sMax, ref int tMax, int rayonDisque)
        {
            var offs = GetDiskOffsets(rayonDisque);
            int n = offs.Count;              // 5, 13, 29...
            int m = imagesErodPrec.Count;    // nombre de bandes

            // Matrices de décision (n pixels-vecteurs x m bandes) pour l'érosion et pour la dilatation
            double[,] gE = new double[n, m];
            double[,] gD = new double[n, m];
            for (int i = 0; i < n; i++)
            {
                int xi = x + offs[i].X, yi = y + offs[i].Y;
                for (int k = 0; k < m; k++)
                {
                    gE[i, k] = imagesErodPrec[k][xi, yi];
                    gD[i, k] = imagesDilatePrec[k][xi, yi];
                }
            }

            // Coefficients de proximité d_p et valeurs f_l de chaque pixel-vecteur du voisinage
            double[] cE = new double[n], flE = new double[n];
            double[] cD = new double[n], flD = new double[n];
            CoefficientsTOPSIS(gE, n, m, poids, normeP, cE, flE);
            CoefficientsTOPSIS(gD, n, m, poids, normeP, cD, flD);

            // Infimum = minimum de l'ordre ; supremum = maximum de l'ordre.
            // L'indice spatial z (ordre de parcours du disque) départage les ex æquo restants :
            //   infimum -> plus petit indice ; supremum -> plus grand indice.
            int idxMin = 0, idxMax = 0;
            for (int z = 1; z < n; z++)
            {
                if (ComparerOrdreTOPSIS(cE[z], flE[z], z, cE[idxMin], flE[idxMin], idxMin) < 0) idxMin = z;
                if (ComparerOrdreTOPSIS(cD[z], flD[z], z, cD[idxMax], flD[idxMax], idxMax) > 0) idxMax = z;
            }
            sMin = x + offs[idxMin].X; tMin = y + offs[idxMin].Y;
            sMax = x + offs[idxMax].X; tMax = y + offs[idxMax].Y;
        }

        // Calcul TOPSIS pour un ensemble de n pixels-vecteurs (matrice de décision g[n, m]) :
        //   c[i]  = coefficient de proximité relative à la solution idéale d_p(a_i)
        //   fl[i] = valeur normalisée pondérée f_l(a_i) sur la bande de poids maximal (critère de bénéfice)
        // Cette fonction est utilisée à la fois pour le voisinage complet (MinMaxVecteurs) et pour la
        // comparaison de deux vecteurs (CompareDeuxVecteursTOPSIS) : une seule définition de l'ordre.
        private void CoefficientsTOPSIS(double[,] g, int n, int m, double[] poids, int p, double[] c, double[] fl)
        {
            // 1. Normalisation et pondération : f_j(a_i) = w_j * g_j(a_i) / racine( somme_l g_j(a_l)^2 )
            double[,] f = new double[n, m];
            for (int k = 0; k < m; k++)
            {
                double s = 0;
                for (int i = 0; i < n; i++) s += g[i, k] * g[i, k];
                double norme = Math.Sqrt(s);
                if (norme == 0) norme = 1; // bande entièrement nulle : f = 0 pour tous les pixels
                for (int i = 0; i < n; i++) f[i, k] = poids[k] * g[i, k] / norme;
            }

            // 2. Polarité des bandes (bénéfice si w_j >= tau = 1/m, sinon coût) puis PIS et NIS
            //    bénéfice : PIS = max, NIS = min ;  coût : PIS = min, NIS = max
            double tau = 1.0 / m;
            double[] pis = new double[m], nis = new double[m];
            for (int k = 0; k < m; k++)
            {
                double mn = f[0, k], mx = f[0, k];
                for (int i = 1; i < n; i++)
                {
                    if (f[i, k] < mn) mn = f[i, k];
                    if (f[i, k] > mx) mx = f[i, k];
                }
                if (poids[k] >= tau - TOL_POLARITE) { pis[k] = mx; nis[k] = mn; }
                else { pis[k] = mn; nis[k] = mx; }
            }

            // 3-4. Distances de Minkowski aux solutions idéales et coefficient de proximité
            for (int i = 0; i < n; i++)
            {
                double sP = 0, sN = 0;
                for (int k = 0; k < m; k++)
                {
                    sP += Math.Pow(Math.Abs(pis[k] - f[i, k]), p);
                    sN += Math.Pow(Math.Abs(nis[k] - f[i, k]), p);
                }
                double dPis = Math.Pow(sP, 1.0 / p);
                double dNis = Math.Pow(sN, 1.0 / p);
                double d = dPis + dNis;
                c[i] = d < EPS ? 0.5 : dNis / d; // d = 0 : pixels indiscernables => coefficient neutre
            }

            // 5. Bande l de poids maximal (critère de bénéfice le plus important) et valeurs f_l
            int l = 0;
            double wmax = poids[0];
            for (int k = 1; k < m; k++)
                if (poids[k] > wmax) { wmax = poids[k]; l = k; }
            for (int i = 0; i < n; i++) fl[i] = f[i, l];
        }

        // Relation d'ordre total TOPSIS entre deux pixels-vecteurs A et B :
        //   d_p d'abord, puis f_l en cas d'égalité, puis l'indice spatial en dernier recours.
        // Retourne -1 si A < B, 1 si A > B, 0 si A et B sont le même pixel.
        private static int ComparerOrdreTOPSIS(double cA, double flA, int idxA, double cB, double flB, int idxB)
        {
            if (Math.Abs(cA - cB) > EPS) return cA < cB ? -1 : 1;
            if (Math.Abs(flA - flB) > EPS) return flA < flB ? -1 : 1;
            if (idxA < idxB) return -1;
            if (idxA > idxB) return 1;
            return 0;
        }

        // ============================== CONVERSION BITMAP -> MATRICES ==============================
        // Une matrice int[x, y] par bande (valeur du canal rouge = niveau de gris)
        private List<int[,]> bmpToMat(List<Bitmap> imagesBmp)
        {
            List<int[,]> imagesMat = new List<int[,]>();
            for (int z = 0; z < imagesBmp.Count; z++)
            {
                imagesMat.Add(new int[imagesBmp[z].Width, imagesBmp[z].Height]);
                for (int x = 0; x < imagesBmp[z].Width; x++)
                    for (int y = 0; y < imagesBmp[z].Height; y++)
                        imagesMat[z][x, y] = imagesBmp[z].GetPixel(x, y).R;
            }
            return imagesMat;
        }
    }
}
