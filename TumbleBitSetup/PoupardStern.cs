﻿using Org.BouncyCastle.Math;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities;
using System;


namespace TumbleBitSetup
{
    public class PoupardStern
    {
        /// <summary>
        /// Proving Algorithm specified in (3.2.1) of the setup
        /// </summary> 
        /// <param name="p">P in the secret key</param>
        /// <param name="q">Q in the secret key</param>
        /// <param name="e">Public Exponent in the public key</param>
        /// <param name="ps">The public string from the setup</param>
        /// <param name="k">Security parameter as specified in the setup.</param>
        /// <returns>List of x values and y</returns>
        public static Tuple<BigInteger[], BigInteger> Proving(BigInteger p, BigInteger q, BigInteger e, int keyLength, byte[] psBytes, int k = 128)
        {
            BigInteger y;
            BigInteger Two = BigInteger.Two;
            // 2^{|N| - 1}
            BigInteger lowerLimit = Two.Pow(keyLength - 1);
            // 2^{|N|}
            BigInteger upperLimit = Two.Pow(keyLength);

            // Rounding up k to the closest multiple of 8
            k = Utils.GetByteLength(k) * 8;

            // Generate a keyPair from p, q and e
            var keyPair = new RsaKey(p, q, e);
            var pubKey = new RsaPubKey(keyPair._pubKey);
            var secKey = keyPair._privKey;

            BigInteger Modulus = pubKey._pubKey.Modulus;

            // Check if N < 2^{|N|-1}
            if (Modulus.CompareTo(lowerLimit) < 0)
                throw new ArgumentOutOfRangeException("Bad RSA modulus N");

            // if N >= 2^{KeySize}
            if (Modulus.CompareTo(upperLimit) >= 0)
                throw new ArgumentOutOfRangeException("Bad RSA modulus N");

            // p and q don't produce a modulus N that has the expected bitLength
            if (!(Modulus.BitLength.Equals(keyLength)))
                throw new ArgumentException("Bad RSA P and Q");

            // Calculating phi
            BigInteger pSub1 = p.Subtract(BigInteger.One);
            BigInteger qSub1 = q.Subtract(BigInteger.One);
            BigInteger phi = pSub1.Multiply(qSub1);
            // N - phi(N)
            BigInteger NsubPhi = Modulus.Subtract(phi);

            // if 2^{|N|-1}/{(N-phi)*2^k} <= 2^k
            var p11 = Two.Pow(k);
            var p1 = lowerLimit.Divide(NsubPhi.Multiply(p11));
            if (p1.CompareTo(p11) <= 0)
                throw new ArgumentOutOfRangeException("Bad RSA modulus N");

            // Generate K
            GetK(k, out int BigK);

            // Initialize and generate list of z values
            BigInteger[] zValues = new BigInteger[BigK];
            for (int i = 0; i < BigK; i++)
                zValues[i] =  SampleFromZnStar(pubKey, psBytes, i, BigK, keyLength);

            for (;;)
            {
                // Initialize list of x values.
                BigInteger[] xValues = new BigInteger[BigK];

                // Generate r
                GetR(keyLength, out BigInteger r);

                for (int i = 0; i < BigK; i++)
                    // Compute x_i
                    xValues[i] = zValues[i].ModPow(r, Modulus);

                // Compute w
                GetW(pubKey, psBytes, xValues, k, keyLength, out BigInteger w);

                // Compute y
                y = r.Add(NsubPhi.Multiply(w));

                // if y >= 2^{ |N| - 1 }
                if (y.CompareTo(lowerLimit) >= 0)
                    continue;
                
                // if y < 0
                if (y.CompareTo(BigInteger.Zero) < 0)
                    continue;

                return new Tuple<BigInteger[], BigInteger>(xValues, y);
            }
            
        }

        /// <summary>
        /// Verifying Algorithm specified in (3.3) of the setup
        /// </summary>
        /// <param name="pubKey">Public key used</param>
        /// <param name="xValues">List of x_i values</param>
        /// <param name="y">The value y as specified in the setup</param>
        /// <param name="keyLength">The size of the RSA key in bits</param>
        /// <param name="ps">public string specified in the setup</param>
        /// <param name="k">Security parameter specified in the setup.</param>
        /// <returns>true if the xValues verify, false otherwise</returns>
        public static bool Verifying(RsaPubKey pubKey, BigInteger[] xValues, BigInteger y, int keyLength, byte[] psBytes, int k = 128)
        {
            BigInteger rPrime;
            BigInteger lowerLimit = BigInteger.Two.Pow(keyLength - 1);
            BigInteger upperLimit = BigInteger.Two.Pow(keyLength);
            
            // Rounding up k to the closest multiple of 8
            k = Utils.GetByteLength(k) * 8;

            var Modulus = pubKey._pubKey.Modulus;
            var Exponent = pubKey._pubKey.Exponent;

            // Checking that:
            // if y >= 2^{ |N| - 1 }
            if (y.CompareTo(lowerLimit) >= 0)  
                return false;
            // if y < 0
            if (y.CompareTo(BigInteger.Zero) < 0)
                return false;
            // if N < 2^{KeySize-1}
            if (Modulus.CompareTo(lowerLimit) < 0)
                return false;
            // if N >= 2^{KeySize}
            if (Modulus.CompareTo(upperLimit) >= 0)
                return false;

            // Computing K
            GetK(k, out int BigK);

            // Check if the number of x_values is not equal to K
            if (xValues.Length != BigK)
                return false;

            // Get w
            GetW(pubKey, psBytes, xValues, k, keyLength, out BigInteger w);

            // Computing rPrime
            rPrime = y.Subtract(Modulus.Multiply(w));

            // Verifying x values
            for (int i = 0; i < BigK; i++)
            {
                var z_i = SampleFromZnStar(pubKey, psBytes, i, BigK, keyLength);
                // Compute right side of the equality
                var rs = z_i.ModPow(rPrime, Modulus);
                // If the two sides are not equal
                if (!(xValues[i].Equals(rs)))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// Generates a z_i value as specified in (3.3.1) of the setup
        /// </summary>
        /// <param name="pubKey">Public key used</param>
        /// <param name="ps">public string specified in the setup</param>
        /// <param name="i">index i</param>
        /// <param name="k">Security parameter specified in the setup.</param>
        /// <param name="keyLength">The size of the RSA key in bits</param>
        /// <returns></returns>
        internal static BigInteger SampleFromZnStar(RsaPubKey pubKey, byte[] psBytes, int i, int BigK, int keyLength)
        {
            BigInteger Modulus = pubKey._pubKey.Modulus;

            // Octet Length of i
            int iLen = Utils.GetOctetLen(BigK);
            // OctetString of i
            var EI = Utils.I2OSP(i, iLen);
            // ASN.1 encoding of the PublicKey
            var keyBytes = pubKey.ToBytes();
            // Combine the OctetString
            // TODO: Combine now takes multiple lists as input, so no need for the double call
            var combined = Utils.Combine(keyBytes, Utils.Combine(psBytes,EI));
            int j = 2;
            for (;;)
            {
                // OctetLength of j
                var jLen = Utils.GetOctetLen(j);
                // OctetString of j
                var EJ = Utils.I2OSP(j, jLen);
                // Combine EJ with the rest of the string
                var sub_combined = Utils.Combine(combined, EJ);
                // Pass the bytes to H_1
                byte[] ER = Utils.MGF1_SHA256(sub_combined, keyLength);
                // Convert from OctetString to BigInteger
                BigInteger z_i = Utils.OS2IP(ER);
                // Check if the output is larger or equal to N OR GCD(z_i, N) != 1
                if (z_i.CompareTo(Modulus) >= 0 || !(z_i.Gcd(Modulus).Equals(BigInteger.One)))
                {
                    j++;
                    continue;
                }
                return z_i;
            }
        }

        /// <summary>
        /// Calculates the value of w as specified in the setup.
        /// </summary>
        /// <param name="pubKey">Public key used</param>
        /// <param name="ps">public string specified in the setup</param>
        /// <param name="xValues"> List of x_i values</param>
        /// <param name="k">Security parameter as specified in the setup.</param>
        /// <param name="keyLength">The size of the RSA key in bits</param>
        internal static void GetW(RsaPubKey pubKey, byte[] psBytes, BigInteger[] xValues, int k, int keyLength, out BigInteger w)
        {
            if (xValues == null)
                throw new ArgumentNullException(nameof(xValues));

            var BigK = xValues.Length;

            // ASN.1 encoding of the PublicKey
            var keyBytes = pubKey.ToBytes();

            // Computing ExLen
            var ExLen = Utils.GetByteLength(keyLength);

            // Encoding the x Values
            byte[] ExComb = new byte[0]; // Empty Array (Initialization)
            for (int i = 0; i < BigK; i++)
            {
                var tmp = Utils.I2OSP(xValues[i], ExLen);
                ExComb = Utils.Combine(ExComb, tmp);
            }
            // Concatenating the rest of s
            // TODO: Combine now takes multiple lists as input, so no need for the double call
            var s = Utils.Combine(keyBytes, Utils.Combine(psBytes, ExComb));
            // Hash the OctetString
            var BigW = Utils.SHA256(s);
            // Truncate to k-bits
            BigW = Utils.TruncateToKbits(BigW, k);
            // Convert to an Integer and return
            w = Utils.OS2IP(BigW);
        }

        /// <summary>
        /// Calculates the value of K as specified in equation 7 of the setup
        /// </summary>
        /// <param name="k">Security parameter specified in the setup</param>
        internal static void GetK(int k, out int BigK)
        {
            BigK = k + 1;
            return;
        }

        /// <summary>
        /// Calculates the value of r as specified in the setup.
        /// </summary>
        /// <param name="keyLength">The size of the RSA key in bits</param>
        internal static void GetR(int keyLength, out BigInteger r)
        {
            // Initialize a cryptographic randomness.
            SecureRandom random = new SecureRandom();

            // bitSize for the random value r.
            int bitSize = keyLength - 1;

            // Generate random number that is bitSize long.
            r = new BigInteger(bitSize, random);

            return;
        }

    }

}