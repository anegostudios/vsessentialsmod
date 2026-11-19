using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Vintagestory.API.Datastructures;
using Vintagestory.API.Util;

namespace Vintagestory.GameContent
{
    public class Genome
    {
        public GenomeType Type  { get; protected set; }

        public readonly int Ploidy = 2;

        // Dimensions are [genes, ploidy]
        public byte[,] Autosomal { get; protected set; }
        public byte[,] Anonymous { get; protected set; }
        public byte[,] XZ { get; protected set; }

        // A compact way to store numerous genes each with only 2 possible alleles
        // Format: Basic layout similar to autosomal genes (gene copies grouped), but compressed down to where each allele is only 1 bit.
        // Across the bytes in the array the order is as you'd expect, starting at index 0 and increasing.
        // Within each byte, the order is right to left - 0th is 0 bitshifts from the right, 1 is one bitshift from the right, etc.
        // So for a typical n=2 creature, the first byte has 2 bits for the two alleles of the first gene, same for the second, third, and fourth, and then the fifth is on the second byte.
        public byte[] Bitwise { get; protected set; }

        public int GetBitwiseAllele(int gene, int n)
        {
            int i = gene * Ploidy + n;
            int b = i % 8;
            return (Bitwise[i/8] >> b) & 1;
        }

        public void SetBitwiseAllele(int gene, int n, int val)
        {
            int i = Ploidy * gene + n;
            int b = i % 8;
            byte v = Bitwise[i/8];
            v = (byte)(v & ~(1 << b)); // Set to 0
            v = (byte)(v | (val << b)); // Now set from 0 to val
            Bitwise[i/8] = v;
        }

        // Checks if any allele matches any of the given alleles, for an autosomal gene
        public bool HasAllele(int gene, params byte[] alleles)
        {
            bool result = false;
            for (int n = 0; n < Ploidy; ++n)
            {
                foreach (byte a in alleles)
                {
                    result = result || Autosomal[gene, n] == a;
                }
            }
            return result;
        }

        // Returns true if all copies of the gene match the passed allele
        public bool IsHomozygous(int gene, byte allele)
        {
            return HasOnlyAlleles(gene, allele);
        }

        // Checks if every allele matches at least one of the given alleles, for an autosomal gene
        public bool HasOnlyAlleles(int gene, params byte[] alleles)
        {
            bool result = true;
            for (int n = 0; n < Ploidy; ++n)
            {
                bool matches = false;
                foreach (byte a in alleles)
                {
                    matches = matches || Autosomal[gene, n] == a;
                }
                result = result && matches;
            }
            return result;
        }

        // Checks if any allele matches any of the given alleles, for a gene on the X or Z chromosome
        public bool HasSexlinked(int gene, params byte[] alleles)
        {
            bool result = false;
            for (int n = 0; n < XZ.GetLength(1); ++n)
            {
                foreach (byte a in alleles)
                {
                    result = result || XZ[gene, n] == a;
                }
            }
            return result;
        }

        // Checks if every allele matches at least one of the given alleles, for a gene on the X or Z chromosome
        public bool HasOnlySexlinked(int gene, params byte[] alleles)
        {
            bool result = true;
            for (int n = 0; n < XZ.GetLength(1); ++n)
            {
                bool matches = false;
                foreach (byte a in alleles)
                {
                    matches = matches || XZ[gene, n] == a;
                }
                result = result && matches;
            }
            return result;
        }

        public bool IsHomogametic()
        {
            return XZ.GetLength(1) == Ploidy;
        }

        public bool IsHeterogametic()
        {
            return !IsHomogametic();
        }

        public int BitwiseSum(Range range)
        {
            int total = 0;
            for (int g = range.Start.Value; g < range.End.Value; ++g)
            {
                for (int n = 0; n < Ploidy; ++n)
                {
                    total += GetBitwiseAllele(g, n);
                }
            }
            return total;
        }

        // Returns the number of genes in the given range where ANY allele is 1
        public int BitwiseDominant(Range range)
        {
            int total = 0;
            for (int g = range.Start.Value; g < range.End.Value; ++g)
            {
                int v = 0;
                for (int n = 0; n < Ploidy; ++n)
                {
                    v |= GetBitwiseAllele(g, n);
                }
                total += v;
            }
            return total;
        }

        // Returns the number of genes in the given range where EVERY allele is 1
        public int BitwiseRecessive(Range range)
        {
            int total = 0;
            for (int g = range.Start.Value; g < range.End.Value; ++g)
            {
                int v = 1;
                for (int n = 0; n < Ploidy; ++n)
                {
                    v &= GetBitwiseAllele(g, n);
                }
                total += v;
            }
            return total;
        }

        public int BitwiseHomozygotes(Range range)
        {
            int total = 0;
            for (int g = range.Start.Value; g < range.End.Value; ++g)
            {
                bool match = true;
                int a = GetBitwiseAllele(g, 0);
                for (int n = 1; n < Ploidy; ++n)
                {
                    match = match && GetBitwiseAllele(g, n) == a;
                }
                if (match) total += 1;
            }
            return total;
        }

        public int BitwiseSum(string geneGroup)
        {
            return BitwiseSum(Type.Bitwise.TryGetRange(geneGroup));
        }

        public int BitwiseDominant(string geneGroup)
        {
            return BitwiseDominant(Type.Bitwise.TryGetRange(geneGroup));
        }

        public int BitwiseRecessive(string geneGroup)
        {
            return BitwiseRecessive(Type.Bitwise.TryGetRange(geneGroup));
        }

        public int BitwiseHomozygotes(string geneGroup)
        {
            return BitwiseHomozygotes(Type.Bitwise.TryGetRange(geneGroup));
        }

        public float BitwiseFraction(string geneGroup)
        {
            Range range = Type.Bitwise.TryGetRange(geneGroup);
            float diff = range.End.Value - range.Start.Value;
            return diff == 0 ? 0 : BitwiseSum(range) / diff / Ploidy;
        }

        public bool HasAllele(string gene, params string[] alleles)
        {
            int geneID = Type.Autosomal.GetGeneID(gene);
            return HasAllele(geneID, alleles.Select(allele => Type.Autosomal.GetAlleleID(geneID, allele)).ToArray());
        }

        public bool IsHomozygous(string gene, string allele)
        {
            int geneID = Type.Autosomal.GetGeneID(gene);
            return HasOnlyAlleles(geneID, Type.Autosomal.GetAlleleID(geneID, allele));
        }

        public bool HasOnlyAlleles(string gene, params string[] alleles)
        {
            int geneID = Type.Autosomal.GetGeneID(gene);
            return HasOnlyAlleles(geneID, alleles.Select(allele => Type.Autosomal.GetAlleleID(geneID, allele)).ToArray());
        }

        public bool HasSexlinked(string gene, params string[] alleles)
        {
            int geneID = Type.XZ.GetGeneID(gene);
            return HasSexlinked(geneID, alleles.Select(allele => Type.XZ.GetAlleleID(geneID, allele)).ToArray());
        }

        public bool HasOnlySexlinked(string gene, params string[] alleles)
        {
            int geneID = Type.XZ.GetGeneID(gene);
            return HasOnlySexlinked(geneID, alleles.Select(allele => Type.XZ.GetAlleleID(geneID, allele)).ToArray());
        }

        public void SetAutosomal(string gene, int n, string allele)
        {
            int geneID = Type.Autosomal.GetGeneID(gene);
            byte alleleID = Type.Autosomal.GetAlleleID(geneID, allele);
            Autosomal[geneID, n] = alleleID;
        }

        public void SetHomozygous(string gene, string allele)
        {
            int geneID = Type.Autosomal.GetGeneID(gene);
            byte alleleID = Type.Autosomal.GetAlleleID(geneID, allele);
            for (int n = 0; n < Ploidy; ++n)
            {
                Autosomal[geneID, n] = alleleID;
            }
        }

        // Ensures the animal is not homozygous for the given alleles, or combinations thereof.
        // If you add any embryonic lethal genes, be sure to call this after spawn
        // to prevent adults from spawning with genes that shouldn't be physically possible.
        public void SetNotHomozygousFor(string gene, string[] avoidAlleles, GeneInitializer? initializer, string? fallbackAllele)
        {
            // If already not homozygous, then we're done and mission accomplished
            if (!HasOnlyAlleles(gene, avoidAlleles)) return;

            int geneID = Type.Autosomal.GetGeneID(gene);
            byte[] avoidIDs = avoidAlleles.Select(allele => Type.Autosomal.GetAlleleID(geneID, allele)).ToArray();


            // Try the first replacement allele with a non-zero spawn chance
            float[]? f = initializer?.AutosomalFrequencies?[geneID];
            if (f != null)
            {
                float prev = 0;
                for (int i = 0; i < f.Length; ++i)
                {
                    if (f[i] > prev && !avoidIDs.Contains((byte) i))
                    {
                        Autosomal[geneID, 0] = (byte) i;
                        return;
                    }
                    prev = f[i];
                }
            }

            if (fallbackAllele != null)
            {
                Autosomal[geneID, 0] = Type.Autosomal.GetAlleleID(geneID, fallbackAllele);
                // Here we are trusting that the caller hasn't passed us a fallbackAllele the same as what we're supposed to avoid
                return;
            }

            // No help provided, fall back to 0
            if (!avoidIDs.Contains((byte) 0))
            {
                Autosomal[geneID, 0] = (byte) 0;
                return;
            }

            throw new Exception("Failed to find a valid replacement for gene " + gene + " in initializer " + (initializer?.Name ?? "null"));
        }

        protected Genome(GenomeType type) : this(type, 2) { }

        protected Genome(GenomeType type, int ploidy)
        {
            Type = type;
            Ploidy = ploidy;

            Autosomal = new byte[Type.Autosomal.GeneCount, Ploidy];
            Anonymous = new byte[Type.Anonymous.GeneCount, Ploidy];

            Bitwise = new byte[(int)Math.Ceiling(Ploidy * Type.Bitwise.GeneCount / 8.0)];

            // Leaves the sex chromosomes uninitialized
            XZ = null!;
        }

        public Genome(GenomeType type, GeneInitializer? initializer, bool heterogametic, Random random) : this(type, 2)
        {
            if (heterogametic)
            {
                XZ = new byte[Type.XZ.GeneCount, Ploidy/2];
            }
            else
            {
                XZ = new byte[Type.XZ.GeneCount, Ploidy];
            }

            byte[] buffer = new byte[Anonymous.GetLength(0) * Anonymous.GetLength(1)];
            random.NextBytes(buffer);
            Buffer.BlockCopy(buffer, 0, Anonymous, 0, buffer.Length);

            // If no initializer, leave all full of zeros
            if (initializer == null) return;

            for (int gene = 0; gene < Type.Autosomal.GeneCount; ++gene)
            {
                for (int n = 0; n < Ploidy; ++n)
                {
                    Autosomal[gene, n] = getRandomAllele(initializer.AutosomalFrequencies?[gene], random);
                }
            }

            int i = 0;
            for (int group = 0; group < Type.Bitwise.GeneGroupCount; ++group)
            {
                float[]? chances = initializer.BitwiseFrequencies?[group];
                for (int gene = 0; gene < Type.Bitwise.GroupSizes[group]; ++gene)
                {
                    float chance = chances?[Math.Min(gene, chances.Length - 1)] ?? 0.5f;
                    for (int n = 0; n < Ploidy; ++n)
                    {
                        if (random.NextSingle() < chance)
                        {
                            Bitwise[i/8] |= (byte) (1 << (i % 8));
                        }
                        i += 1;
                    }
                }
            }

            for (int gene = 0; gene < Type.XZ.GeneCount; ++gene)
            {
                for (int n = 0; n < XZ.GetLength(1); ++n)
                {
                    XZ[gene, n] = getRandomAllele(initializer.SexlinkedFrequencies?[gene], random);
                }
            }
        }

        /// <summary>
        /// Chooses an allele at random from the given options, and returns its byte ID.
        /// Pass in an array of cumulative likelihoods. For example, given alleles A, B, C, D:
        /// 40% chance of A, 20% chance of B, 30% chance of C, 10% chance of D
        /// the float values you pass in would be [0.4, 0.6, 0.9, 1.0]
        /// </summary>
        protected byte getRandomAllele(float[]? alleles, Random random)
        {
            if (alleles == null)
            {
                return 0;
            }
            float f = random.NextSingle();
            byte a = 0;
            for ( ; a < alleles.Length && alleles[a] < f; ++a);

            return (byte)Math.Min(a, alleles.Length - 1);
        }

        // In the unlikely circumstance that anybody wants to make a mod supporting polyploidy,
        // AND wants chromosome pairing to be properly random, you'll want to override or harmony patch this function
        public virtual Genome CreateGamete(bool heterogametic, Random random)
        {
            if (Ploidy % 2 != 0)
            {
                throw new InvalidOperationException("Not supported for odd ploidy (n=" + Ploidy + "). Genome type: " + Type.Code);
            }
            Genome gamete = new Genome(Type, Ploidy / 2);

            SplitGenes(gamete.Autosomal, Autosomal, Type.AutosomalLinkages, random);
            SplitGenes(gamete.Anonymous, Anonymous, null, random);

            for (int p = 0; p < gamete.Ploidy; ++p)
            {
                for (int gene = 0; gene < Type.Bitwise.GeneCount; ++gene)
                {
                    int n = random.Next(2);
                    gamete.SetBitwiseAllele(gene, p, GetBitwiseAllele(gene, 2 * p + n));
                }
            }

            if (this.IsHeterogametic())
            {
                if (heterogametic)
                {
                    gamete.XZ = new byte[Type.XZ.GeneCount, 0];
                }
                else
                {
                    gamete.XZ = (byte[,])this.XZ.Clone();
                }
            }
            else
            {
                gamete.XZ = new byte[Type.XZ.GeneCount, gamete.Ploidy];
                SplitGenes(gamete.XZ, XZ, Type.SexChromosomeLinkages, random);
            }

            return gamete;
        }

        protected static void SplitGenes(byte[,] gamete, byte[,] parent, GeneLinkage[]? linkages, Random random)
        {
            for (int p = 0; p < gamete.GetLength(1); ++p)
            {
                int n = random.Next(2);
                for (int gene = 0; gene < parent.GetLength(0); ++gene)
                {
                    gamete[gene, p] = parent[gene, 2 * p + n];
                    n = random.Next(2);
                }
            }

            if (linkages == null) return;

            // A second pass covering only linked genes
            for (int p = 0; p < gamete.GetLength(1); ++p)
            {
                int n = random.Next(2);
                foreach (GeneLinkage linkage in linkages)
                {
                    gamete[linkage.GeneID, p] = parent[linkage.GeneID, 2 * p + n];
                    if (random.NextSingle() < linkage.CrossoverChance)
                    {
                        n = 1 - n;
                    }
                }
            }
        }

        public virtual Genome Join(Genome other)
        {
            Genome zygote = new Genome(Type, this.Ploidy + other.Ploidy);
            JoinGenes(zygote.Autosomal, this.Autosomal, other.Autosomal);
            JoinGenes(zygote.Anonymous, this.Anonymous, other.Anonymous);

            for (int gene = 0; gene < Type.Bitwise.GeneCount; ++gene)
            {
                for (int p = 0; p < this.Ploidy; ++p)
                {
                    zygote.SetBitwiseAllele(gene, p, GetBitwiseAllele(gene, p));
                }
                for (int p = 0; p < other.Ploidy; ++p)
                {
                    zygote.SetBitwiseAllele(gene, p + this.Ploidy, other.GetBitwiseAllele(gene, p));
                }
            }

            zygote.XZ = new byte[Type.XZ.GeneCount, this.XZ.GetLength(1) + other.XZ.GetLength(1)];
            JoinGenes(zygote.XZ, this.XZ, other.XZ);
            return zygote;
        }

        protected static void JoinGenes(byte[,] joined, byte[,] first, byte[,] second)
        {
            ArgumentNullException.ThrowIfNull(joined);
            ArgumentNullException.ThrowIfNull(first);
            ArgumentNullException.ThrowIfNull(second);
            for (int gene = 0; gene < first.GetLength(0); ++gene)
            {
                for (int p = 0; p < first.GetLength(1); ++p)
                {
                    joined[gene, p] = first[gene, p];
                }
                for (int p = 0; p < second.GetLength(1); ++p)
                {
                    joined[gene, first.GetLength(1) + p] = second[gene, p];
                }
            }
        }

        public static Genome Inherit(Genome mother, Genome father, bool isHeterogametic, Random random)
        {
            return mother.CreateGamete(isHeterogametic, random).Join(father.CreateGamete(isHeterogametic, random));
        }

        /// <summary>
        /// Randomly changes the genes.
        /// Pass in the chance for each gene to change, a randomness source, and optionally
        /// a GeneInitializer with every allowed mutation result set to a non-zero chance.
        /// If you pass null for the gene initializer, all alleles defined by the genome type will be allowed.
        /// The allowed list only restricts autosomal and sexlinked, not bitwise or anonymous genes.
        /// </summary>
        public virtual Genome Mutate(double p, Random random, GeneInitializer? allowed)
        {
            for (int gene = 0; gene < Type.Autosomal.GeneCount; ++gene)
            {
                for (int n = 0; n < Ploidy; ++n)
                {
                    if (random.NextDouble() < p)
                    {
                        byte result = (byte) random.Next(Type.Autosomal.AlleleCount(gene));
                        int tries = 0;
                        while (allowed != null && !GeneInitializer.AllelePossible(allowed.AutosomalFrequencies, gene, result) && tries < 100)
                        {
                            result = (byte) random.Next(Type.Autosomal.AlleleCount(gene));
                            ++tries;
                        }
                        if (tries < 100)
                        {
                            Autosomal[gene, n] = result;
                        }
                    }
                }
            }
            for (int gene = 0; gene < Type.Anonymous.GeneCount; ++gene)
            {
                for (int n = 0; n < Ploidy; ++n)
                {
                    if (random.NextDouble() < p)
                    {
                        Anonymous[gene, n] = (byte)random.Next(256);
                    }
                }
            }
            for (int i = 0; i < Bitwise.Length; ++i)
            {
                for (int b = 0; b < 8; ++b)
                {
                    if (random.NextDouble() < p)
                    {
                        Bitwise[i] ^= (byte)(1 << b);
                    }
                }
            }
            for (int gene = 0; gene < Type.XZ.GeneCount; ++gene)
            {
                for (int n = 0; n < XZ.GetLength(1); ++n)
                {
                    if (random.NextDouble() < p)
                    {
                        byte result = (byte) random.Next(Type.XZ.AlleleCount(gene));
                        int tries = 0;
                        while (allowed != null && !GeneInitializer.AllelePossible(allowed.SexlinkedFrequencies, gene, result) && tries < 100)
                        {
                            result = (byte) random.Next(Type.XZ.AlleleCount(gene));
                            ++tries;
                        }
                        if (tries < 100)
                        {
                            XZ[gene, n] = result;
                        }
                    }
                }
            }
            return this;
        }

        private byte[] atLeastSize(byte[]? given, int count)
        {
            if (given != null && given.Length >= count)
            {
                return given;
            }
            byte[] array = new byte[count];
            if (given == null)
            {
                return array;
            }
            Array.Copy(given, array, given.Length);
            return array;
        }

        private byte[,] atLeastSize(byte[,]? given, int count)
        {
            if (given != null && given.GetLength(0) >= count)
            {
                return given;
            }
            byte[,] array = new byte[count, given?.GetLength(1) ?? Ploidy];
            if (given == null)
            {
                return array;
            }
            Array.Copy(given, array, given.Length);
            return array;
        }

        public Genome(GenomeType type, ITreeAttribute geneticsTree)
        {
            this.Type = type;

            // Note these values could temporarily be null, until the rest of the constructor logic runs
            Autosomal = geneticsTree.Get2DByteArray("autosomal")!;
            Anonymous = geneticsTree.Get2DByteArray("anonymous")!;
            Bitwise = geneticsTree.GetBytes("bitwise")!;
            XZ = geneticsTree.Get2DByteArray("xz")!;

            Ploidy = (Autosomal ?? Anonymous)?.GetLength(1) ?? 2;

            bool heterogametic = geneticsTree.GetBool("heterogametic");
            XZ ??= new byte[0, heterogametic ? Ploidy/2 : Ploidy]; // Need to set correct ploidy for it here, then length gets handled later

            this.Autosomal = atLeastSize(Autosomal, Type.Autosomal.GeneCount);
            this.Anonymous = atLeastSize(Anonymous, Type.Anonymous.GeneCount);
            this.Bitwise = atLeastSize(Bitwise, (int)Math.Ceiling(Ploidy * Type.Bitwise.GeneCount / 8.0));
            this.XZ = atLeastSize(XZ, Type.XZ.GeneCount);
        }

        // Caller is responsible for marking the path as dirty if necessary
        public void AddToTree(ITreeAttribute geneticsTree)
        {
            if (Autosomal.Length == 0)
            {
                geneticsTree.RemoveAttribute("autosomal");
            }
            else
            {
                geneticsTree.Set2DByteArray("autosomal", Autosomal);
            }
            if (Anonymous.Length == 0)
            {
                geneticsTree.RemoveAttribute("anonymous");
            }
            else
            {
                geneticsTree.Set2DByteArray("anonymous", Anonymous);
            }
            if (Bitwise.Length == 0)
            {
                geneticsTree.RemoveAttribute("bitwise");
            }
            else
            {
                geneticsTree.SetBytes("bitwise", Bitwise);
            }
            // Set XZ regardless of whether the length is 0 or not, because we want to know how many X or Z chromosomes are present
            // A mod could add sex-linked genes later, so it's useful even if there are none now
            geneticsTree.Set2DByteArray("xz", XZ);
            geneticsTree.SetBool("heterogametic", XZ.GetLength(1) != Ploidy);
        }

        public override string ToString()
        {
            return "Genome << type:" + Type.Code 
                + ",\n    autosomal=" + Autosomal.ArrayToString() 
                + ",\n    xz=" + XZ.ArrayToString() 
                + ",\n    anonymous=" + Anonymous.ArrayToString()
                + ",\n    bitwise=" + Bitwise.ArrayToString() + " >>";
        }
    }
}
